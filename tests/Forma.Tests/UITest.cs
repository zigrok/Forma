// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Forma;
using NUnit.Framework;

namespace Forma.Tests
{
    public class UITest
    {
        [TestCase(1f, .5f, 2f)]
        [TestCase(1f, .85f, 1f / .85f)]
        [TestCase(1f, 1f, 1f)]
        [TestCase(1f, 1.7f, 1f)]
        [TestCase(2f, .85f, 2f)]
        public void VisibleHairlineThickness_CoversAtLeastOnePhysicalPixel(float thickness, float displayScale, float expected)
        {
            Assert.That(UIRenderContext.GetVisibleHairlineThickness(thickness, displayScale), Is.EqualTo(expected).Within(.0001f));
        }

        private static readonly GameTime Time = new GameTime();

        private sealed class TestClipboard : IClipboard
        {
            public string Text { get; set; }
            public string GetText() => Text;
            public bool SetText(string text) { Text = text; return true; }
        }

        private static MouseState Mouse(int x, int y, ButtonState left = ButtonState.Released, ButtonState right = ButtonState.Released, ButtonState middle = ButtonState.Released, ButtonState xButton1 = ButtonState.Released, ButtonState xButton2 = ButtonState.Released, int scrollWheel = 0) => new MouseState(x, y, scrollWheel, left, middle, right, xButton1, xButton2);

        private static Texture2D CreateHeadlessTexture(int width, int height)
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            SetTextureDimension(texture, nameof(Texture2D.Width), "width", width);
            SetTextureDimension(texture, nameof(Texture2D.Height), "height", height);
            return texture;
        }

        private static void SetTextureDimension(Texture2D texture, string propertyName, string fieldName, int value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var property = typeof(Texture2D).GetProperty(propertyName, flags)!;
            var setter = property.GetSetMethod(true);
            if (setter != null) setter.Invoke(texture, [value]);
            else typeof(Texture2D).GetField(fieldName, flags)!.SetValue(texture, value);
            if ((int)property.GetValue(texture)! != value) throw new InvalidOperationException($"Failed to set Texture2D.{propertyName}.");
        }

        /// <summary>Builds a headless SpriteFont (no GraphicsDevice/texture needed for MeasureString) spanning the same 32-126 ASCII range and no DefaultCharacter as this project's bundled test font, to exercise Font.MeasureString deterministically.</summary>
        private static SpriteFont CreateTestFont()
        {
            var characters = new List<char>();
            var bounds = new List<Rectangle>();
            var cropping = new List<Rectangle>();
            var kerning = new List<Vector3>();
            for (var c = (char)32; c <= (char)126; c++)
            {
                characters.Add(c);
                bounds.Add(new Rectangle(0, 0, 8, 16));
                cropping.Add(new Rectangle(0, 0, 8, 16));
                kerning.Add(new Vector3(0, 8, 0));
            }
            return (SpriteFont)Activator.CreateInstance(typeof(SpriteFont), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [null, bounds, cropping, characters, 16, 0f, kerning, null], null)!;
        }

        [Test]
        public void PointerClick_ActivatesTopmostButtonAndGivesItFocus()
        {
            var context = new UIContext();
            var root = new Panel { Size = new Vector2(200, 100) };
            var button = new Button { Position = new Vector2(10, 10), Size = new Vector2(80, 30) };
            var clicks = 0;
            button.Pressed += (_, _) => clicks++;
            root.AddChild(button);
            context.Add(root);

            context.Update(Time, Mouse(20, 20), new KeyboardState());
            context.Update(Time, Mouse(20, 20, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(20, 20), new KeyboardState());

            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(context.FocusedControl, Is.SameAs(button));
        }

        [Test]
        public void GroupBox_MeasuresAndArrangesHeaderAboveContent()
        {
            var content = new Control { CustomMinimumSize = new Vector2(160, 60) };
            var group = new GroupBox
            {
                Title = "Key bindings",
                Content = content,
                Padding = new Thickness(10),
                HeaderIndent = 8,
                HeaderGap = 4,
                BorderWidth = 1,
                Size = new Vector2(220, 120),
            };
            var headerPresenter = (Label)group.GetTemplateChild(GroupBox.HeaderPresenterPartName);
            headerPresenter.CustomMinimumSize = new Vector2(90, 20);
            using var context = new UIContext { ViewportSize = group.Size };
            context.Add(group);
            context.Layout();

            var contentPresenter = (ContentPresenter)group.GetTemplateChild(ContentControl.ContentPresenterPartName);
            Assert.Multiple(() =>
            {
                Assert.That(group.GetMinimumSize(), Is.EqualTo(new Vector2(182, 101)));
                Assert.That(headerPresenter.Position, Is.EqualTo(new Vector2(9, 0)));
                Assert.That(contentPresenter.Position.Y, Is.EqualTo(30));
                Assert.That(contentPresenter.Size.X, Is.EqualTo(198));
            });
        }

        [Test]
        public void GroupBox_UsesStringHeaderAsAccessibleName()
        {
            var group = new GroupBox { Title = "Key bindings" };

            Assert.That(group.AccessibilityRole, Is.EqualTo(AccessibilityRole.Group));
            Assert.That(group.AccessibilityName, Is.EqualTo("Key bindings"));

            group.Name = "BindingsGroup";
            Assert.That(group.AccessibilityName, Is.EqualTo("Key bindings"));

            group.AccessibilityLabel = "Keyboard controls";
            Assert.That(group.AccessibilityName, Is.EqualTo("Keyboard controls"));
        }

        [Test]
        public void PointerHover_ActivatesVisualAncestors()
        {
            var context = new UIContext();
            var parent = new Panel { Size = new Vector2(100, 100) };
            var child = new Control { Position = new Vector2(10, 10), Size = new Vector2(40, 40) };
            parent.AddChild(child);
            context.Add(parent);

            context.Update(Time, Mouse(20, 20), new KeyboardState());

            Assert.Multiple(() =>
            {
                Assert.That(child.IsPseudoStateActive("hover"), Is.True);
                Assert.That(parent.IsPseudoStateActive("hover"), Is.True);
            });
        }

        [Test]
        public void UIContext_DisplayScaleMapsPhysicalPointerToLogicalControls()
        {
            var context = new UIContext { DisplayScale = 2f };
            var button = new Button { Position = new Vector2(10, 10), Size = new Vector2(20, 20) };
            var clicks = 0;
            button.Pressed += (_, _) => clicks++;
            context.Add(button);

            context.Update(Time, Mouse(40, 40), new KeyboardState());
            context.Update(Time, Mouse(40, 40, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(40, 40), new KeyboardState());

            Assert.That(context.PointerPosition, Is.EqualTo(new Point(20, 20)));
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(context.FocusedControl, Is.SameAs(button));
        }

        [Test]
        public void InjectPointerClick_UsesPhysicalPixelsAndActivatesControl()
        {
            var context = new UIContext { DisplayScale = 2f };
            var button = new Button { Position = new Vector2(10, 10), Size = new Vector2(20, 20) };
            var clicks = 0;
            button.Pressed += (_, _) => clicks++;
            context.Add(button);

            context.InjectPointerMove(new Point(40, 40));
            context.InjectPointerPress(new Point(40, 40));
            context.InjectPointerRelease(new Point(40, 40));

            Assert.That(context.PointerPosition, Is.EqualTo(new Point(20, 20)));
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(context.FocusedControl, Is.SameAs(button));
        }

        [Test]
        public void InjectPointerWheel_RoutesDeltaWithoutChangingPolledWheelBaseline()
        {
            var context = new UIContext();
            var scroll = new ScrollContainer { Size = new Vector2(100, 100) };
            scroll.AddChild(new Control { CustomMinimumSize = new Vector2(100, 400) });
            context.Add(scroll);
            context.Update(Time, Mouse(50, 50, scrollWheel: 100), new KeyboardState());

            Assert.That(context.InjectPointerWheel(new Point(50, 50), -120), Is.True);
            var afterInjection = scroll.VerticalScroll;
            context.Update(Time, Mouse(50, 50, scrollWheel: 100), new KeyboardState());

            Assert.That(afterInjection, Is.GreaterThan(0f));
            Assert.That(scroll.VerticalScroll, Is.EqualTo(afterInjection));
        }

        [Test]
        public void ButtonGroup_EnforcesExclusiveToggleAndOptionalUnpress()
        {
            var group = new ButtonGroup();
            var first = new Button { ToggleMode = true, ButtonGroup = group, Size = new Vector2(80, 30) };
            var second = new Button { ToggleMode = true, ButtonGroup = group, Position = new Vector2(90, 0), Size = new Vector2(80, 30) };
            var toggles = 0;
            first.Toggled += (_, _) => toggles++;
            second.Toggled += (_, _) => toggles++;

            first.ButtonPressed = true;
            second.ButtonPressed = true;
            second.ButtonPressed = false;
            second.ButtonPressed = true;
            var context = new UIContext();
            context.Add(first); context.Add(second);
            context.Update(Time, Mouse(100, 10), new KeyboardState());
            context.Update(Time, Mouse(100, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(100, 10), new KeyboardState());
            Assert.That(second.ButtonPressed, Is.True, "A user cannot clear the only selected group button by default.");
            group.AllowUnpress = true;
            context.Update(Time, Mouse(100, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(100, 10), new KeyboardState());

            Assert.That(group.Buttons, Has.Count.EqualTo(2));
            Assert.That(first.ButtonPressed, Is.False);
            Assert.That(second.ButtonPressed, Is.False);
            Assert.That(group.PressedButton, Is.Null);
            Assert.That(toggles, Is.EqualTo(7));
        }

        [Test]
        public void ButtonActionMode_PressActivatesOnlyOnceBeforeRelease()
        {
            var context = new UIContext();
            var button = new Button { Size = new Vector2(80, 30), ActionMode = ButtonActionMode.Press };
            var presses = 0;
            button.Pressed += (_, _) => presses++;
            context.Add(button);

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());

            Assert.That(presses, Is.EqualTo(1));
        }

        [Test]
        public void BaseButton_CustomTemplateReplacesChromeWithoutReplacingInputSemantics()
        {
            var context = new UIContext();
            var button = new Button { Size = new Vector2(80, 30), Text = "Action" };
            var presses = 0;
            button.Pressed += (_, _) => presses++;
            context.Add(button);
            context.Layout();
            var packagedRoot = button.TemplateRoot;

            button.Template = ControlTemplate.Create<Button>((_, _) => new Border
            {
                Background = new SolidColorBrush(Color.CornflowerBlue),
            });
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(button.TemplateRoot, Is.TypeOf<Border>());
                Assert.That(button.TemplateRoot, Is.Not.SameAs(packagedRoot));
                Assert.That(button.GetTemplateChild(ContentControl.ContentPresenterPartName), Is.Null);
                Assert.That(typeof(BaseButton).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), Is.Null);
                Assert.That(context.HitTest(new Point(10, 10)), Is.SameAs(button));
            });

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            button.GrabFocus();
            context.Update(Time, Mouse(100, 100), new KeyboardState(Keys.Enter));
            context.Update(Time, Mouse(100, 100), new KeyboardState());

            Assert.That(presses, Is.EqualTo(2));
        }

        [Test]
        public void BaseButton_KeepPressedOutsideAffectsOnlyVisualStateNotActivation()
        {
            // Verified against Godot source: BaseButton::on_action_event gates real activation solely on
            // status.pressing_inside (recomputed from the live cursor position); keep_pressed_outside is
            // read only by get_draw_mode() for the DRAW_PRESSED visual, never by the activation check.
            var context = new UIContext();
            var button = new Button { Size = new Vector2(80, 30), KeepPressedOutside = true };
            var downs = 0; var ups = 0; var presses = 0;
            button.ButtonDown += (_, _) => downs++;
            button.ButtonUp += (_, _) => ups++;
            button.Pressed += (_, _) => presses++;
            context.Add(button);

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(button.IsVisuallyPressed, Is.True, "Pressed while the cursor is still inside.");

            context.Update(Time, Mouse(120, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(button.IsVisuallyPressed, Is.True, "keep_pressed_outside keeps the DRAW_PRESSED visual once dragged outside the bounds.");

            context.Update(Time, Mouse(120, 10), new KeyboardState());
            Assert.That(downs, Is.EqualTo(1));
            Assert.That(ups, Is.EqualTo(1));
            Assert.That(presses, Is.EqualTo(0), "Releasing outside the bounds must not activate the button, even with keep_pressed_outside set.");
            Assert.That(button.IsVisuallyPressed, Is.False, "Releasing clears the visual pressed state regardless of keep_pressed_outside.");

            button.KeepPressedOutside = false;
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(120, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(button.IsVisuallyPressed, Is.False, "Without keep_pressed_outside, dragging outside clears the visual pressed state immediately.");
            context.Update(Time, Mouse(120, 10), new KeyboardState());
            Assert.That(presses, Is.EqualTo(0));
        }

        [Test]
        public void BaseButton_MapsGodotShortcutActivationFeedbackAndTooltipState()
        {
            var context = new UIContext();
            var group = new ButtonGroup();
            var first = new Button { Size = new Vector2(80, 30), ToggleMode = true, ButtonGroup = group, Text = "First", TooltipText = "First action" };
            var second = new Button { Position = new Vector2(90, 0), Size = new Vector2(80, 30), ToggleMode = true, ButtonGroup = group, Text = "Second" };
            first.SetShortcut(new PopupMenuShortcut("First action", Keys.K, control: true));
            second.SetShortcut(new PopupMenuShortcut("Second action", Keys.L, control: true));
            var pressed = 0; var toggles = new List<(BaseButton Button, bool Pressed)>();
            first.Pressed += (_, _) => pressed++;
            first.Toggled += (button, isPressed) => toggles.Add((button, isPressed));
            second.Toggled += (button, isPressed) => toggles.Add((button, isPressed));
            context.Add(first); context.Add(second);

            Assert.That(first.GetShortcut().DisplayText, Is.EqualTo("Ctrl+K"));
            Assert.That(first.IsShortcutInTooltipEnabled(), Is.True);
            Assert.That(first.GetTooltip(Point.Zero), Is.EqualTo("First action (Ctrl+K)"));
            first.SetShortcutInTooltip(false);
            Assert.That(first.GetTooltip(Point.Zero), Is.EqualTo("First action"));
            first.SetShortcutInTooltip(true);

            context.Update(Time, Mouse(300, 40), new KeyboardState());
            context.Update(Time, Mouse(300, 40), new KeyboardState(Keys.LeftControl, Keys.K));
            Assert.That(pressed, Is.EqualTo(1));
            Assert.That(first.ButtonPressed, Is.True);
            Assert.That(group.PressedButton, Is.SameAs(first));
            Assert.That(first.IsShortcutFeedbackActive, Is.True);

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.25)), Mouse(300, 40), new KeyboardState());
            Assert.That(first.IsShortcutFeedbackActive, Is.False);

            context.Update(Time, Mouse(300, 40), new KeyboardState(Keys.LeftControl, Keys.L));
            Assert.That(first.ButtonPressed, Is.False);
            Assert.That(second.ButtonPressed, Is.True);
            Assert.That(group.PressedButton, Is.SameAs(second));

            first.SetShortcutFeedback(false);
            context.Update(Time, Mouse(300, 40), new KeyboardState());
            context.Update(Time, Mouse(300, 40), new KeyboardState(Keys.LeftControl, Keys.K));
            Assert.That(first.ButtonPressed, Is.True);
            Assert.That(first.IsShortcutFeedbackActive, Is.False);

            first.SetDisabled(true);
            context.Update(Time, Mouse(300, 40), new KeyboardState());
            context.Update(Time, Mouse(300, 40), new KeyboardState(Keys.LeftControl, Keys.K));
            Assert.That(pressed, Is.EqualTo(2));
            Assert.That(first.ButtonPressed, Is.True);
            Assert.That(first.IsDisabled(), Is.True);
            Assert.That(toggles.Count, Is.EqualTo(5));
        }

        [Test]
        public void BaseButton_IsPressedReflectsTransientHoldForNonToggleButtonsLikeGodot()
        {
            // Godot's BaseButton::is_pressed() returns toggle_mode ? status.pressed : status.press_attempt,
            // so a plain (non-toggle) button reports true while physically held, not permanently false.
            var button = new Button { Size = new Vector2(80, 30) };
            var context = new UIContext(); context.Add(button);
            Assert.That(button.IsPressed(), Is.False);

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(button.IsPressed(), Is.True, "A non-toggle button reports pressed while the pointer holds it down.");

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            Assert.That(button.IsPressed(), Is.False, "Releasing clears the transient pressed state.");

            var toggle = new CheckBox { Size = new Vector2(80, 20) };
            var toggleContext = new UIContext(); toggleContext.Add(toggle);
            toggleContext.Update(Time, Mouse(0, 0), new KeyboardState());
            toggleContext.Update(Time, Mouse(0, 0, ButtonState.Pressed), new KeyboardState());
            toggleContext.Update(Time, Mouse(0, 0), new KeyboardState());
            Assert.That(toggle.IsPressed(), Is.True, "For a toggle button, is_pressed reflects the persisted toggled state instead.");
        }

        [Test]
        public void BaseButton_DisabledBlocksKeyboardActivationLikeGodotGuiInput()
        {
            // Godot's BaseButton::gui_input returns immediately for every input kind, including the
            // ui_accept action, whenever the button is disabled.
            var button = new Button { Size = new Vector2(80, 30) };
            var presses = 0; button.Pressed += (_, _) => presses++;
            var context = new UIContext(); context.Add(button); button.GrabFocus();

            button.SetDisabled(true);
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Enter));
            Assert.That(context.FocusedControl, Is.Null, "Disabling a control releases its keyboard focus.");
            Assert.That(presses, Is.EqualTo(0), "A disabled button must ignore Enter/Space.");

            button.SetDisabled(false);
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            Assert.That(context.FocusedControl, Is.Null, "Re-enabling does not steal keyboard focus.");
            button.GrabFocus();
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Enter));
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            Assert.That(presses, Is.EqualTo(1), "The default action_mode (release) activates on the Enter key-up, matching Godot's on_action_event.");
        }

        [Test]
        public void BaseButton_KeyboardActivationHonorsActionModeAndFiresButtonDownUpSeparately()
        {
            // Verified against Godot's on_action_event: the ui_accept action (Enter/Space) flows through
            // the exact same action_mode/button_down/button_up funnel as mouse and touch input; this
            // requires a key-release notification the UI dispatch layer previously never sent at all.
            var button = new Button { Size = new Vector2(80, 30) };
            var downs = 0; var ups = 0; var presses = 0;
            button.ButtonDown += (_, _) => downs++;
            button.ButtonUp += (_, _) => ups++;
            button.Pressed += (_, _) => presses++;
            var context = new UIContext(); context.Add(button); button.GrabFocus();

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Enter));
            Assert.That(downs, Is.EqualTo(1));
            Assert.That(presses, Is.EqualTo(0), "The default release action_mode must not activate on key-down.");
            Assert.That(button.IsVisuallyPressed, Is.True, "A keyboard-held button is always visually pressed, matching Godot's forced pressing_inside for the accept action.");

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            Assert.That(ups, Is.EqualTo(1));
            Assert.That(presses, Is.EqualTo(1), "Release-mode activates on key-up.");
            Assert.That(button.IsVisuallyPressed, Is.False);

            button.ActionMode = ButtonActionMode.Press;
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Space));
            Assert.That(downs, Is.EqualTo(2));
            Assert.That(presses, Is.EqualTo(2), "Press-mode activates immediately on key-down.");

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            Assert.That(ups, Is.EqualTo(2));
            Assert.That(presses, Is.EqualTo(2), "Press-mode does not re-activate on the subsequent key-up.");
        }

        [Test]
        public void ButtonGroup_RefiresPressedSignalWhenReactivatingTheAlreadyPressedMemberLikeGodot()
        {
            // Verified against Godot source: on_action_event always calls button_group->emit_signal("pressed", this)
            // after _unpress_group() reasserts pressed=true for a non-allow-unpress group, even though the
            // button's own state doesn't actually change on this re-click.
            var group = new ButtonGroup();
            var first = new Button { Size = new Vector2(80, 30), ToggleMode = true, ButtonGroup = group };
            var second = new Button { Position = new Vector2(90, 0), Size = new Vector2(80, 30), ToggleMode = true, ButtonGroup = group };
            var groupPresses = new List<BaseButton>(); group.Pressed += (_, button) => groupPresses.Add(button);
            var context = new UIContext(); context.Add(first); context.Add(second);

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            Assert.That(groupPresses, Is.EqualTo(new[] { first }));

            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            Assert.That(first.ButtonPressed, Is.True, "Re-clicking the active member of a non-allow-unpress group leaves it pressed.");
            Assert.That(groupPresses, Is.EqualTo(new[] { first, first }), "Godot still re-fires the group's pressed signal for this re-click even though nothing changed.");
        }

        [Test]
        public void BaseButton_MapsGodotButtonMaskForPointerActivation()
        {
            var context = new UIContext();
            var button = new Button { Size = new Vector2(80, 30) };
            var presses = 0; var downs = 0; var ups = 0;
            button.Pressed += (_, _) => presses++;
            button.ButtonDown += (_, _) => downs++;
            button.ButtonUp += (_, _) => ups++;
            context.Add(button);

            Assert.That(button.GetButtonMask(), Is.EqualTo(ButtonMouseMask.Left));
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, right: ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            Assert.That(presses, Is.EqualTo(0));

            button.SetButtonMask(ButtonMouseMask.Right | ButtonMouseMask.Middle);
            Assert.That(button.GetButtonMask(), Is.EqualTo(ButtonMouseMask.Right | ButtonMouseMask.Middle));
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            Assert.That(presses, Is.EqualTo(0), "Left clicks no longer activate when the left mask is absent.");

            context.Update(Time, Mouse(10, 10, right: ButtonState.Pressed), new KeyboardState());
            Assert.That(button.IsPressing, Is.True);
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            Assert.That(presses, Is.EqualTo(1));
            Assert.That(button.IsPressing, Is.False);

            button.ActionMode = ButtonActionMode.Press;
            context.Update(Time, Mouse(10, 10, middle: ButtonState.Pressed), new KeyboardState());
            Assert.That(presses, Is.EqualTo(2));
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            Assert.That(presses, Is.EqualTo(2), "Press action mode should not re-activate on release.");
            Assert.That(downs, Is.EqualTo(2));
            Assert.That(ups, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => button.SetButtonMask((ButtonMouseMask)8));
        }

        [Test]
        public void TextureButton_CalculatesGodotStretchModesAndClickMasks()
        {
            var button = new TextureButton { Size = new Vector2(100, 50) };
            button.SetStretchMode(TextureButtonStretchMode.Keep);
            var keep = button.GetTextureLayout(new Vector2(20, 10));
            button.SetStretchMode(TextureButtonStretchMode.KeepCentered);
            var centered = button.GetTextureLayout(new Vector2(20, 10));
            button.SetStretchMode(TextureButtonStretchMode.KeepAspectCentered);
            var aspect = button.GetTextureLayout(new Vector2(100, 100));
            button.SetStretchMode(TextureButtonStretchMode.KeepAspectCovered);
            var covered = button.GetTextureLayout(new Vector2(100, 100));
            button.SetStretchMode(TextureButtonStretchMode.Scale);
            var mask = new TextureButtonClickMask(2, 2); mask[0, 0] = true; mask[1, 1] = true;
            button.SetClickMask(mask); button.Position = new Vector2(10, 10); button.Size = new Vector2(20, 20);
            button.SetFlipH(true);
            button.SetFlipV(true);

            Assert.That(keep.Destination, Is.EqualTo(new Rectangle(0, 0, 20, 10)));
            Assert.That(centered.Destination, Is.EqualTo(new Rectangle(40, 20, 20, 10)));
            Assert.That(aspect.Destination, Is.EqualTo(new Rectangle(25, 0, 50, 50)));
            Assert.That(covered.Source, Is.EqualTo(new Rectangle(0, 25, 100, 50)));
            Assert.That(button.GetStretchMode(), Is.EqualTo(TextureButtonStretchMode.Scale));
            Assert.That(button.GetClickMask(), Is.SameAs(mask));
            Assert.That(button.IsFlippedH(), Is.True);
            Assert.That(button.IsFlippedV(), Is.True);
            Assert.That(button.GetMinimumSize(), Is.EqualTo(new Vector2(2, 2)));
            button.SetIgnoreTextureSize(true);
            Assert.That(button.GetIgnoreTextureSize(), Is.True);
            Assert.That(button.GetMinimumSize(), Is.EqualTo(Vector2.Zero));
            Assert.That(button.GetTextureNormal(), Is.Null);
            button.SetTextureNormal(null);
            button.SetTexturePressed(null);
            button.SetTextureHover(null);
            button.SetTextureDisabled(null);
            button.SetTextureFocused(null);
            Assert.That(button.GetTexturePressed(), Is.Null);
            Assert.That(button.GetTextureHover(), Is.Null);
            Assert.That(button.GetTextureDisabled(), Is.Null);
            Assert.That(button.GetTextureFocused(), Is.Null);
            Assert.That(button.ContainsPoint(new Point(11, 11)), Is.True);
            Assert.That(button.ContainsPoint(new Point(25, 11)), Is.False);
            Assert.That(button.ContainsPoint(new Point(25, 25)), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => button.SetStretchMode((TextureButtonStretchMode)999));
        }

        [Test]
        public void TextureButton_KeepAspectTruncatesLikeGodotsIntegerArithmeticNotRounding()
        {
            // Godot's TextureButton::_notification computes STRETCH_KEEP_ASPECT the same two-pass way as
            // TextureRect: first assume the texture fills the full height and derive width proportionally
            // (C++ truncation); 3*100/2=150 overflows the 100-wide control, so it clamps width to 100 and
            // recomputes height via pure integer division: 2*100/3=66 (truncated), not rounded to 67.
            var button = new TextureButton { Size = new Vector2(100, 100), StretchMode = TextureButtonStretchMode.KeepAspect };
            var layout = button.GetTextureLayout(new Vector2(3, 2));
            Assert.That(layout.Destination, Is.EqualTo(new Rectangle(0, 0, 100, 66)));
        }

        [Test]
        public void TextureRect_ExposesGodotExpandModesAndAspectCoveredSourceRegion()
        {
            var rect = new TextureRect { Size = new Vector2(100, 50), StretchMode = TextureStretchMode.KeepAspectCovered };
            var covered = rect.GetTextureLayout(new Vector2(100, 100));
            rect.SetStretchMode(TextureStretchMode.KeepAspectCentered);
            var centered = rect.GetTextureLayout(new Vector2(100, 100));
            rect.SetExpandMode(TextureRectExpandMode.FitWidthProportional);
            rect.Size = new Vector2(100, 40);
            rect.SetFlipH(true);
            rect.SetFlipV(true);
            rect.SetModulate(Color.CornflowerBlue);

            Assert.That(covered.Destination, Is.EqualTo(new Rectangle(0, 0, 100, 50)));
            Assert.That(covered.Source, Is.EqualTo(new Rectangle(0, 25, 100, 50)));
            Assert.That(centered.Destination, Is.EqualTo(new Rectangle(25, 0, 50, 50)));
            Assert.That(rect.GetStretchMode(), Is.EqualTo(TextureStretchMode.KeepAspectCentered));
            Assert.That(rect.GetExpandMode(), Is.EqualTo(TextureRectExpandMode.FitWidthProportional));
            Assert.That(rect.IsFlippedH(), Is.True);
            Assert.That(rect.IsFlippedV(), Is.True);
            Assert.That(rect.GetModulate(), Is.EqualTo(Color.CornflowerBlue));
            Assert.That(rect.GetTextureMinimumSize(new Vector2(80, 40)), Is.EqualTo(new Vector2(80, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => rect.SetStretchMode((TextureStretchMode)999));
            Assert.Throws<ArgumentOutOfRangeException>(() => rect.SetExpandMode((TextureRectExpandMode)999));
        }

        [Test]
        public void TextureRect_KeepAspectTruncatesLikeGodotsIntegerArithmeticNotRounding()
        {
            // Godot's TextureRect::_notification computes STRETCH_KEEP_ASPECT in two passes using C++
            // int truncation, not rounding: first assume the texture fills the full height and derive
            // width proportionally; here 3*100/2=150 overflows the 100-wide control, so it clamps width
            // to 100 and recomputes height via pure integer division: 2*100/3=66 (truncated), not 67.
            var rect = new TextureRect { Size = new Vector2(100, 100), StretchMode = TextureStretchMode.KeepAspect };
            var layout = rect.GetTextureLayout(new Vector2(3, 2));
            Assert.That(layout.Destination, Is.EqualTo(new Rectangle(0, 0, 100, 66)));
        }

        [Test]
        public void ThemeIconRect_UsesLogicalSizeWithoutOwningTheAtlasTexture()
        {
            var texture = CreateHeadlessTexture(64, 64);
            var icon = new ThemeIcon(texture, new Rectangle(8, 12, 32, 24), new Point(16, 12), 2);
            var display = new ThemeIconRect { Icon = icon };

            Assert.That(display.GetMinimumSize(), Is.EqualTo(new Vector2(16, 12)));
            display.Icon = null;
            Assert.That(display.GetMinimumSize(), Is.EqualTo(Vector2.Zero));

            var inheritedTheme = new Theme();
            inheritedTheme.SetIcon("foundation", icon, nameof(ThemeIconView));
            var parent = new Control { ThemeOverride = inheritedTheme };
            var inherited = new ThemeIconView { ThemeItemName = "foundation", ThemeTypeName = nameof(ThemeIconView) };
            parent.AddChild(inherited);
            Assert.That(inherited.GetMinimumSize(), Is.EqualTo(new Vector2(16, 12)));
            Assert.That(texture.IsDisposed, Is.False);
        }

        [Test]
        public void Shape_DirectStrokePropertiesDriveHitGeometry()
        {
            var line = new LineShape
            {
                Stroke = new SolidColorBrush(Color.White),
                StrokeThickness = 4,
                StartPoint = new Vector2(10, 10),
                EndPoint = new Vector2(30, 10),
                StrokeStartLineCap = StrokeLineCap.Square,
                StrokeEndLineCap = StrokeLineCap.Square,
                StrokeDashArray = new[] { 5f, 10f },
            };

            Assert.That(line.ContainsPoint(new Point(9, 10)), Is.True, "The direct start-cap property must extend the first dash.");
            Assert.That(line.ContainsPoint(new Point(20, 10)), Is.False, "The direct dash-array property must leave the first gap empty.");
            Assert.That(line.StrokeStyle, Is.Not.Null);
            Assert.That(line.StrokeStyle.StartLineCap, Is.EqualTo(StrokeLineCap.Square));
            Assert.That(line.StrokeStyle.DashArray, Is.EqualTo(new[] { 5f, 10f }));
        }

        [Test]
        public void TextureRectAndNinePatchRect_DefaultToGodotsMouseFilterFromTheirConstructors()
        {
            Assert.That(new TextureRect().MouseFilter, Is.EqualTo(MouseFilter.Pass), "Godot's TextureRect() constructor calls set_mouse_filter(MOUSE_FILTER_PASS).");
            Assert.That(new NinePatchRect().MouseFilter, Is.EqualTo(MouseFilter.Ignore), "Godot's NinePatchRect() constructor calls set_mouse_filter(MOUSE_FILTER_IGNORE), fully click-through.");
            Assert.That(new TextureProgressBar().MouseFilter, Is.EqualTo(MouseFilter.Pass), "Godot's TextureProgressBar() constructor also calls set_mouse_filter(MOUSE_FILTER_PASS), matching its TextureRect siblings.");
        }

        [Test]
        public void NinePatchRect_TileFitRoundsTheTileCountToTheNearestIntegerNotUpLikeGodot()
        {
            // Godot's real AXIS_STRETCH_MODE_TILE_FIT tile count (canvas.glsl) rounds to the nearest
            // integer: floor(src/dst + 0.5). A 100px source stretched into a 130px destination has a
            // ratio of 1.3, whose nearest integer is 1 (visibly stretched), not 2 (visibly squeezed).
            Assert.That(NinePatchRect.GetTileCount(100, 130, NinePatchAxisStretchMode.TileFit), Is.EqualTo(1));
            Assert.That(NinePatchRect.GetTileCount(100, 151, NinePatchAxisStretchMode.TileFit), Is.EqualTo(2), "151/100 = 1.51 rounds up to 2.");
            Assert.That(NinePatchRect.GetTileCount(100, 149, NinePatchAxisStretchMode.TileFit), Is.EqualTo(1), "149/100 = 1.49 rounds down to 1.");
            // Plain Tile mode keeps using ceiling, since it only needs enough untouched-size tiles to
            // cover the destination without gaps, not a specific stretched quantity.
            Assert.That(NinePatchRect.GetTileCount(100, 130, NinePatchAxisStretchMode.Tile), Is.EqualTo(2));
        }

        [Test]
        public void NinePatchRect_FiresTextureChangedWhenTheTextureActuallyChangesLikeGodot()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var patch = new NinePatchRect();
            var changes = 0;
            patch.TextureChanged += (_, _) => changes++;

            patch.Texture = texture;
            Assert.That(changes, Is.EqualTo(1));

            patch.Texture = texture;
            Assert.That(changes, Is.EqualTo(1), "Godot's set_texture dedups against the current value before emitting texture_changed.");

            patch.SetTexture(null);
            Assert.That(changes, Is.EqualTo(2));
        }

        [Test]
        public void TextureProgressBar_RadialClipUsesTheTexturesOwnUvSpaceNotTheDisplayRectLikeGodot()
        {
            // Godot's unit_val_to_uv clips against a literal unit square using a center normalized into
            // the TEXTURE's own [0,1] space, then rescales the resulting UV per-axis afterward. For a
            // non-square (100,40) texture with a perfectly centered radial center, a 0-to-90-degree
            // clockwise sweep (quarter turn) crosses the 45-degree corner lattice point, which - because
            // it's clipped in UNIT-SQUARE space - always lands exactly on the UV corner (1,0), not a
            // point derived from a direct (100,40)-rect clip.
            var bar = new TextureProgressBar { FillMode = TextureProgressFillMode.Clockwise, Value = 25, MinValue = 0, MaxValue = 100 };
            var textureSize = new Vector2(100, 40);

            var polygon = bar.GetRadialFillPolygon(textureSize);

            Assert.That(polygon.Count, Is.EqualTo(3));
            Assert.That(polygon[0].X, Is.EqualTo(50).Within(.01)); Assert.That(polygon[0].Y, Is.EqualTo(0).Within(.01));
            Assert.That(polygon[1].X, Is.EqualTo(100).Within(.01)); Assert.That(polygon[1].Y, Is.EqualTo(0).Within(.01));
            Assert.That(polygon[2].X, Is.EqualTo(100).Within(.01)); Assert.That(polygon[2].Y, Is.EqualTo(20).Within(.01));
        }

        [Test]
        public void NinePatchRect_ResolvesRegionsMarginsAndAxisPolicies()
        {
            var patch = new NinePatchRect
            {
                PatchMargin = new Thickness(3, 4, 5, 6),
                RegionRect = new Rectangle(2, 3, 20, 16),
                DrawCenter = false,
                HorizontalAxisStretchMode = NinePatchAxisStretchMode.Tile,
                VerticalAxisStretchMode = NinePatchAxisStretchMode.TileFit,
            };

            Assert.That(patch.GetMinimumSize(), Is.EqualTo(new Vector2(8, 10)));
            Assert.That(patch.GetSourceRegion(new Vector2(16, 16)), Is.EqualTo(new Rectangle(2, 3, 14, 13)));
            Assert.That(patch.DrawCenter, Is.False);
            Assert.That(patch.HorizontalAxisStretchMode, Is.EqualTo(NinePatchAxisStretchMode.Tile));
            Assert.That(patch.VerticalAxisStretchMode, Is.EqualTo(NinePatchAxisStretchMode.TileFit));

            patch.SetPatchMargin(Side.Left, 7);
            patch.SetPatchMargin(Side.Bottom, 9);
            patch.SetRegionRect(new Rectangle(1, 2, 30, 20));
            patch.SetDrawCenter(true);
            patch.SetHAxisStretchMode(NinePatchAxisStretchMode.Stretch);
            patch.SetVAxisStretchMode(NinePatchAxisStretchMode.Tile);

            Assert.That(patch.GetPatchMargin(Side.Left), Is.EqualTo(7));
            Assert.That(patch.GetPatchMargin(Side.Bottom), Is.EqualTo(9));
            Assert.That(patch.GetPatchMargins(), Is.EqualTo(new Thickness(7, 4, 5, 9)));
            Assert.That(patch.GetRegionRect(), Is.EqualTo(new Rectangle(1, 2, 30, 20)));
            Assert.That(patch.IsDrawCenterEnabled(), Is.True);
            Assert.That(patch.GetHAxisStretchMode(), Is.EqualTo(NinePatchAxisStretchMode.Stretch));
            Assert.That(patch.GetVAxisStretchMode(), Is.EqualTo(NinePatchAxisStretchMode.Tile));
            Assert.Throws<ArgumentOutOfRangeException>(() => patch.GetPatchMargin((Side)999));
            Assert.Throws<ArgumentOutOfRangeException>(() => patch.SetHAxisStretchMode((NinePatchAxisStretchMode)999));
        }

        [Test]
        public void LinkButton_ExposesGodotUnderlineAndUriStateWithoutChangingButtonBehavior()
        {
            var link = new LinkButton { Text = "Documentation", Uri = "https://example.com", UnderlineMode = LinkButtonUnderlineMode.OnHover };

            Assert.That(link.ToggleMode, Is.False);
            Assert.That(link.Uri, Is.EqualTo("https://example.com"));
            Assert.That(link.UnderlineMode, Is.EqualTo(LinkButtonUnderlineMode.OnHover));
        }

        [Test]
        public void LinkButton_RequestsUriThroughOptionalHostCapability()
        {
            var requested = new List<string>();
            var launched = new List<Uri>();
            var link = new LinkButton
            {
                Uri = "https://example.com/docs",
                Size = new Vector2(100, 30),
                UriLauncher = (_, uri) => { launched.Add(uri); return true; },
            };
            link.UriRequested += (_, uri) => requested.Add(uri);
            var context = new UIContext();
            context.Add(link);

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());

            Assert.That(link.IsUriLaunchingAvailable, Is.True);
            Assert.That(requested, Is.EqualTo(new[] { "https://example.com/docs" }));
            Assert.That(launched.Single().AbsoluteUri, Is.EqualTo("https://example.com/docs"));

            link.UriLauncher = (_, _) => throw new PlatformNotSupportedException();
            Assert.DoesNotThrow(() =>
            {
                context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
                context.Update(Time, Mouse(10, 10), new KeyboardState());
            });
            link.UriLauncher = null;
            Assert.That(link.IsUriLaunchingAvailable, Is.False);
        }

        [Test]
        public void Label_MapsGodotPublicTextStateAndLineQueries()
        {
            var label = new Label { Text = "one|two|three", ParagraphSeparator = "|", Padding = new Thickness(0), Size = new Vector2(120, 48) };

            Assert.That(label.GetTextDirection(), Is.EqualTo(TextDirection.Auto));
            Assert.That(label.GetVisibleCharactersBehavior(), Is.EqualTo(LabelVisibleCharactersBehavior.CharactersBeforeShaping));
            Assert.That(label.GetLineCount(), Is.EqualTo(3));
            Assert.That(label.GetVisibleLineCount(), Is.EqualTo(3));
            Assert.That(label.GetTotalCharacterCount(), Is.EqualTo("one|two|three".Length));

            label.SetLinesSkipped(1);
            label.SetMaxLinesVisible(1);
            Assert.That(label.GetDisplayLines(), Is.EqualTo(new[] { "two" }));
            Assert.That(label.GetVisibleLineCount(), Is.EqualTo(1));
            Assert.That(label.GetLineCount(), Is.EqualTo(3));

            label.SetVisibleCharacters(3);
            Assert.That(label.GetVisibleCharacters(), Is.EqualTo(3));
            Assert.That(label.GetVisibleRatio(), Is.EqualTo(3f / "one|two|three".Length).Within(0.0001f));

            label.SetVisibleRatio(1);
            Assert.That(label.GetVisibleCharacters(), Is.EqualTo(-1));
            Assert.That(label.GetVisibleRatio(), Is.EqualTo(1));

            label.SetTextDirection(TextDirection.RightToLeft);
            label.SetLanguage("ar");
            label.SetStructuredTextBidiOverride(StructuredTextParser.Uri);
            label.SetStructuredTextBidiOverrideOptions(new object[] { "scheme", "host" });
            label.SetTabStops(new[] { 24f, 48f });
            label.SetClipText(true);
            label.SetUppercase(true);
            label.SetEllipsisChar("...");

            Assert.That(label.GetTextDirection(), Is.EqualTo(TextDirection.RightToLeft));
            Assert.That(label.GetLanguage(), Is.EqualTo("ar"));
            Assert.That(label.GetStructuredTextBidiOverride(), Is.EqualTo(StructuredTextParser.Uri));
            Assert.That(label.GetStructuredTextBidiOverrideOptions(), Is.EqualTo(new object[] { "scheme", "host" }));
            Assert.That(label.GetTabStops(), Is.EqualTo(new[] { 24f, 48f }));
            Assert.That(label.IsClippingText(), Is.True);
            Assert.That(label.IsUppercase(), Is.True);
            Assert.That(label.GetEllipsisChar(), Is.EqualTo("..."));
            Assert.Throws<ArgumentOutOfRangeException>(() => label.SetLinesSkipped(-1));
        }

        [Test]
        public void Label_MapsGodotJustificationParagraphSpacingAndCharacterBounds()
        {
            var label = new Label { Text = "one|two", ParagraphSeparator = "|", Position = new Vector2(10, 20), Padding = new Thickness(3), ParagraphSpacing = 5 };

            Assert.That(label.GetJustificationFlags(), Is.EqualTo(LabelJustificationFlags.Kashida | LabelJustificationFlags.WordBound | LabelJustificationFlags.SkipLastLine | LabelJustificationFlags.DoNotSkipSingleLine));

            label.SetJustificationFlags(LabelJustificationFlags.WordBound | LabelJustificationFlags.AfterLastTab);
            Assert.That(label.GetJustificationFlags(), Is.EqualTo(LabelJustificationFlags.WordBound | LabelJustificationFlags.AfterLastTab));
            Assert.That(label.GetParagraphSpacing(), Is.EqualTo(5));
            label.SetParagraphSpacing(-3);
            Assert.That(label.GetParagraphSpacing(), Is.EqualTo(0));
            label.SetParagraphSpacing(5);

            Assert.That(label.GetCharacterBounds(1), Is.EqualTo(new Rectangle(11, 3, 8, 16)));
            Assert.That(label.GetCharacterBounds(3), Is.EqualTo(Rectangle.Empty), "The paragraph separator itself does not map to a glyph rectangle.");
            Assert.That(label.GetCharacterBounds(4), Is.EqualTo(new Rectangle(3, 24, 8, 16)));

            label.SetLinesSkipped(1);

            Assert.That(label.GetCharacterBounds(1), Is.EqualTo(Rectangle.Empty));
            Assert.That(label.GetCharacterBounds(4), Is.EqualTo(new Rectangle(3, 3, 8, 16)));
            Assert.That(label.GetCharacterBounds(-1), Is.EqualTo(Rectangle.Empty));
            Assert.That(label.GetCharacterBounds(99), Is.EqualTo(Rectangle.Empty));
        }

        [Test]
        public void Label_LimitsVisibleLinesToItsArrangedHeight()
        {
            var label = new Label { Text = "A|B|C", ParagraphSeparator = "|", Padding = new Thickness(0), Size = new Vector2(100, 31) };

            Assert.That(label.GetLineCount(), Is.EqualTo(3));
            Assert.That(label.GetDisplayLines(), Is.EqualTo(new[] { "A" }));
            Assert.That(label.GetVisibleLineCount(), Is.EqualTo(1));
        }

        [Test]
        public void Label_MapsWrappedCharactersAndParagraphSpacingToVisualLines()
        {
            var label = new Label
            {
                Text = "A B|C",
                ParagraphSeparator = "|",
                Font = CreateTestFont(),
                AutowrapMode = LabelAutowrapMode.Word,
                Padding = new Thickness(0),
                ParagraphSpacing = 5,
                Size = new Vector2(16, 100),
            };

            Assert.That(label.GetCharacterBounds(0).Y, Is.EqualTo(0));
            Assert.That(label.GetCharacterBounds(2).Y, Is.EqualTo(16));
            Assert.That(label.GetCharacterBounds(4).Y, Is.EqualTo(37));
        }

        [Test]
        public void Label_DynamicFontUsesRetainedLayoutForMeasureWrapAndCharacterBounds()
        {
            using var latinFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var label = new Label
            {
                UIFont = new DynamicUIFont(latinFace, 18, UIFontHinting.Default, arabicFace),
                Text = "Forma مرحبا Forma",
                AutowrapMode = LabelAutowrapMode.Word,
                TextDirection = TextDirection.Auto,
                Language = "ar",
                Padding = new Thickness(0),
                Size = new Vector2(90, 80)
            };

            Assert.Multiple(() =>
            {
                Assert.That(label.GetMinimumSize().Y, Is.GreaterThan(0));
                Assert.That(label.GetLineCount(), Is.GreaterThan(1));
                Assert.That(label.GetLineHeight(), Is.GreaterThan(0));
                Assert.That(label.GetCharacterBounds(0), Is.Not.EqualTo(Rectangle.Empty));
                Assert.That(label.GetCharacterBounds(7), Is.Not.EqualTo(Rectangle.Empty));
            });
        }

        [Test]
        public void ButtonAndProgressBarMeasureDynamicFontsThroughSharedLayouts()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 18);
            var button = new Button { UIFont = font, Text = "Dynamic button" };
            var progress = new ProgressBar { UIFont = font, Value = 50 };

            Assert.Multiple(() =>
            {
                Assert.That(button.GetMinimumSize().X, Is.GreaterThan(button.Padding.Horizontal));
                Assert.That(button.GetMinimumSize().Y, Is.GreaterThan(button.Padding.Vertical));
                Assert.That(progress.UIFont, Is.SameAs(font));
                Assert.That(progress.Font, Is.Null);
            });
        }

        [Test]
        public void TextControlsResolveDynamicFontFamilyAndSizeThroughParentTheme()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var inherited = new Theme
            {
                FontFamily = new UIFontFamily(new[] { new DynamicUIFont(face, 12) }),
                FontSize = 20,
                FontHinting = UIFontHinting.Auto,
                FontOpenTypeFeatures = new[] { new UIFontOpenTypeFeature("kern", 0) }
            };
            var context = new UIContext { Theme = new Theme { Parent = inherited } };
            var button = new Button { Text = "Inherited" };
            var label = new Label { Text = "AV", Padding = new Thickness(0) };
            context.Add(button);
            context.Add(label);

            var themedFont = (DynamicUIFont)button.EffectiveUIFont;
            var themedLayout = new TextLayoutEngine().Layout(themedFont, label.Text);

            Assert.Multiple(() =>
            {
                Assert.That(button.UIFont, Is.Null);
                Assert.That(themedFont.Size, Is.EqualTo(20));
                Assert.That(themedFont.Hinting, Is.EqualTo(UIFontHinting.Auto));
                Assert.That(themedLayout.Options.OpenTypeFeatures, Is.EqualTo(new[] { new UIFontOpenTypeFeature("kern", 0) }));
                Assert.That(label.GetLineHeight(), Is.GreaterThan(12));
            });

            label.SetOpenTypeFeatures(new[] { new UIFontOpenTypeFeature("kern", 1) });
            Assert.That(label.GetMinimumSize().X, Is.LessThan(themedLayout.Size.X));

            var local = new DynamicUIFont(face, 14);
            button.UIFont = local;
            Assert.That(button.EffectiveUIFont, Is.SameAs(local));
        }

        [Test]
        public void TextLayoutInvalidatesForFontLocaleDirectionWidthAndThemeButNotDensity()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var context = new UIContext
            {
                Theme = new Theme { FontFamily = new UIFontFamily(new[] { new DynamicUIFont(face, 12) }) }
            };
            var localTheme = new Theme { FontSize = 18 };
            var label = new Label
            {
                ThemeOverride = localTheme,
                Text = "AV shaped text",
                AutowrapMode = LabelAutowrapMode.Word,
                Size = new Vector2(160, 40)
            };
            context.Add(label);
            context.Layout();
            var layouts = 0;
            label.LayoutChanged += (_, _) => layouts++;

            localTheme.FontSize = 24;
            context.Layout();
            Assert.That(label.EffectiveUIFont.Size, Is.EqualTo(24));

            localTheme.FontOpenTypeFeatures = new[] { new UIFontOpenTypeFeature("kern", 0) };
            context.Layout();
            Assert.That(new TextLayoutEngine().Layout(label.EffectiveUIFont, "AV").Options.OpenTypeFeatures, Is.EqualTo(new[] { new UIFontOpenTypeFeature("kern", 0) }));

            label.UIFont = new DynamicUIFont(face, 20);
            context.Layout();
            label.Language = "en-US";
            context.Layout();
            label.TextDirection = TextDirection.LeftToRight;
            context.Layout();
            label.Size = new Vector2(90, 40);
            context.Layout();

            var layoutsBeforeDensityChange = layouts;
            var logicalSizeBeforeDensityChange = label.GetMinimumSize();
            context.DisplayScale = 2;
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(layoutsBeforeDensityChange, Is.GreaterThanOrEqualTo(6));
                Assert.That(layouts, Is.EqualTo(layoutsBeforeDensityChange));
                Assert.That(label.GetMinimumSize(), Is.EqualTo(logicalSizeBeforeDensityChange));
            });
        }

        [Test]
        public void LineEditDynamicLayoutMovesAndDeletesWholeGraphemes()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var lineEdit = new LineEdit { UIFont = new DynamicUIFont(face, 18), Text = "A😀e\u0301" };
            lineEdit.KeyPressed(Keys.End);

            lineEdit.KeyPressed(Keys.Left);
            Assert.That(lineEdit.CaretColumn, Is.EqualTo(3));
            lineEdit.KeyPressed(Keys.Left);
            Assert.That(lineEdit.CaretColumn, Is.EqualTo(1));
            lineEdit.KeyPressed(Keys.Delete);
            Assert.That(lineEdit.Text, Is.EqualTo("Ae\u0301"));
        }

        [Test]
        public void LineEditKeepsShapedCaretVisibleAndHitTestsThroughHorizontalScroll()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var lineEdit = new LineEdit
            {
                UIFont = new DynamicUIFont(face, 18),
                Text = "A😀e\u0301 long shaped text",
                Size = new Vector2(90, 28)
            };

            lineEdit.KeyPressed(Keys.End);
            var endOffset = lineEdit.GetScrollOffset();
            lineEdit.PointerPressed(new Point((int)lineEdit.Padding.Left + 1, 10));

            Assert.Multiple(() =>
            {
                Assert.That(endOffset, Is.GreaterThan(0));
                Assert.That(lineEdit.CaretColumn, Is.GreaterThan(0));
            });

            lineEdit.KeyPressed(Keys.Home);
            Assert.That(lineEdit.GetScrollOffset(), Is.Zero);
        }

        [Test]
        public void LineEditVerticallyAlignsSingleLineTextWithinPadding()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var lineEdit = new LineEdit
            {
                UIFont = new DynamicUIFont(face, 18),
                Text = "Aligned",
                Padding = new Thickness(6, 4, 6, 4),
                Size = new Vector2(160, 64),
            };
            var layout = lineEdit.GetEditingLayout();
            var spareHeight = lineEdit.Size.Y - lineEdit.Padding.Vertical - layout.Size.Y;

            Assert.Multiple(() =>
            {
                Assert.That(lineEdit.TextVerticalAlignment, Is.EqualTo(VerticalAlignment.Center));
                Assert.That(lineEdit.GetTextVerticalOffset(layout), Is.EqualTo(spareHeight * .5f).Within(.01f));
            });

            lineEdit.TextVerticalAlignment = VerticalAlignment.Top;
            Assert.That(lineEdit.GetTextVerticalOffset(layout), Is.Zero);
            lineEdit.TextVerticalAlignment = VerticalAlignment.Bottom;
            Assert.That(lineEdit.GetTextVerticalOffset(layout), Is.EqualTo(spareHeight).Within(.01f));
            lineEdit.TextVerticalAlignment = VerticalAlignment.Fill;
            Assert.That(lineEdit.GetTextVerticalOffset(layout), Is.Zero);
            Assert.That(() => lineEdit.TextVerticalAlignment = (VerticalAlignment)99, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void LineEditKeepsImePreeditSeparateAndCommitsItsReplacementRange()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var context = new UIContext();
            var lineEdit = new LineEdit { UIFont = new DynamicUIFont(face, 18), Text = "Cafe" };
            context.Add(lineEdit);
            lineEdit.GrabFocus();
            lineEdit.Select(3, 4);

            context.TextComposition("e\u0301", 0, 2);

            Assert.Multiple(() =>
            {
                Assert.That(lineEdit.Text, Is.EqualTo("Cafe"));
                Assert.That(lineEdit.ImeCompositionText, Is.EqualTo("e\u0301"));
                Assert.That(lineEdit.ImeCompositionSelection, Is.EqualTo(new Point(0, 2)));
            });

            lineEdit.CommitImeComposition();
            Assert.Multiple(() =>
            {
                Assert.That(lineEdit.Text, Is.EqualTo("Cafe\u0301"));
                Assert.That(lineEdit.CaretColumn, Is.EqualTo(5));
                Assert.That(lineEdit.HasImeComposition, Is.False);
            });

            context.TextComposition("x", 0, 1);
            context.TextInput('X');
            Assert.That(lineEdit.Text, Is.EqualTo("Cafe\u0301X"));
        }

        [Test]
        public void LineEditShapesMixedScriptTextThroughFallbackRuns()
        {
            using var latin = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabic = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var lineEdit = new LineEdit
            {
                UIFont = new DynamicUIFont(latin, 18, UIFontHinting.Default, arabic),
                Text = "AمرحباB"
            };

            var layout = lineEdit.GetEditingLayout();
            lineEdit.KeyPressed(Keys.End);
            lineEdit.KeyPressed(Keys.Left);

            Assert.Multiple(() =>
            {
                Assert.That(layout.Runs.Select(run => run.Font.Identity).Distinct().Count(), Is.GreaterThanOrEqualTo(2));
                Assert.That(lineEdit.CaretColumn, Is.EqualTo(lineEdit.Text.Length - 1));
            });
        }

        [Test]
        public void Label_AppliesVerticalAlignmentToCharacterBounds()
        {
            var label = new Label { Text = "A|B|C", ParagraphSeparator = "|", Padding = new Thickness(0), Size = new Vector2(100, 80) };

            label.VerticalAlignment = VerticalAlignment.Center;
            Assert.That(label.GetCharacterBounds(0).Y, Is.EqualTo(16));
            label.VerticalAlignment = VerticalAlignment.Bottom;
            Assert.That(label.GetCharacterBounds(0).Y, Is.EqualTo(32));
            label.VerticalAlignment = VerticalAlignment.Fill;
            Assert.That(new[] { label.GetCharacterBounds(0).Y, label.GetCharacterBounds(2).Y, label.GetCharacterBounds(4).Y }, Is.EqualTo(new[] { 0, 32, 64 }));
        }

        [Test]
        public void Label_AppliesGodotTabStopsAndSingleLineFillJustificationToGlyphLayout()
        {
            var label = new Label { Font = CreateTestFont(), Padding = new Thickness(0), Size = new Vector2(40, 20), Text = "A\tB" };
            label.SetTabStops(new[] { 24f });

            Assert.That(label.GetCharacterBounds(2).X, Is.EqualTo(24), "A tab advances to the next repeated tab-stop interval.");

            label.Text = "A B";
            label.HorizontalAlignment = HorizontalAlignment.Fill;

            Assert.That(label.GetCharacterBounds(2).X, Is.EqualTo(32), "Godot's default DoNotSkipSingleLine flag allows a one-line paragraph to fill its width.");

            label.Text = "A B|C D";
            label.ParagraphSeparator = "|";
            label.Size = new Vector2(40, 40);
            Assert.That(label.GetCharacterBounds(6).X, Is.EqualTo(32), "Single-line justification is decided per paragraph, not across the whole label.");

            label.SetJustificationFlags(LabelJustificationFlags.WordBound | LabelJustificationFlags.SkipLastLine);
            Assert.That(label.GetCharacterBounds(6).X, Is.EqualTo(16), "Without DoNotSkipSingleLine, the only line in a paragraph remains unfilled.");
        }

        [Test]
        public void ProgressBar_MapsGodotFillDirectionsAndIndeterminateState()
        {
            var progress = new ProgressBar { Size = new Vector2(102, 22), Value = 25 };
            progress.SetFillMode(ProgressBarFillMode.EndToBegin);
            var endToBegin = progress.GetFillRectangle(progress.Ratio);
            progress.SetFillMode(ProgressBarFillMode.BottomToTop);
            var bottomToTop = progress.GetFillRectangle(progress.Ratio);
            progress.SetIndeterminate(true);
            var elapsed = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            progress.Process(elapsed);
            var offsetAfterProcess = progress.IndeterminateOffset;
            progress.SetFillMode(ProgressBarFillMode.TopToBottom);

            Assert.That(endToBegin, Is.EqualTo(new Rectangle(76, 1, 25, 20)));
            Assert.That(bottomToTop, Is.EqualTo(new Rectangle(1, 16, 100, 5)));
            Assert.That(offsetAfterProcess, Is.GreaterThan(0));
            Assert.That(progress.IndeterminateOffset, Is.EqualTo(0), "Godot resets indeterminate fill progress when fill mode changes.");
            Assert.That(progress.GetFillMode(), Is.EqualTo(ProgressBarFillMode.TopToBottom));
            Assert.That(progress.IsIndeterminate(), Is.True);
            Assert.That(progress.IsPercentageShown(), Is.True);
            progress.SetShowPercentage(false);
            progress.SetEditorPreviewIndeterminate(true);
            Assert.That(progress.IsPercentageShown(), Is.False);
            Assert.That(progress.IsEditorPreviewIndeterminateEnabled(), Is.True);
            progress.Process(elapsed);
            Assert.That(progress.IndeterminateOffset, Is.GreaterThan(0));
            progress.SetIndeterminate(false);
            Assert.That(progress.IsIndeterminate(), Is.False);
            Assert.That(progress.IndeterminateOffset, Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.SetFillMode((ProgressBarFillMode)999));
        }

        [Test]
        public void RangeControls_CustomTemplatesPreserveOwnerStateAndSliderInput()
        {
            var context = new UIContext();
            var slider = new HSlider
            {
                Size = new Vector2(100, 20),
                Value = 20,
                Template = ControlTemplate.Create<Slider>((_, _) => new Border
                {
                    Background = new SolidColorBrush(Color.DarkSeaGreen),
                }),
            };
            var progress = new ProgressBar
            {
                Position = new Vector2(0, 30),
                Size = new Vector2(100, 20),
                Value = 35,
                Template = ControlTemplate.Create<ProgressBar>((_, _) => new Border
                {
                    Background = new SolidColorBrush(Color.CornflowerBlue),
                }),
            };
            context.Add(slider);
            context.Add(progress);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(slider.TemplateRoot, Is.TypeOf<Border>());
                Assert.That(progress.TemplateRoot, Is.TypeOf<Border>());
                Assert.That(progress.Value, Is.EqualTo(35));
                Assert.That(context.HitTest(new Point(75, 10)), Is.SameAs(slider));
                Assert.That(typeof(Slider).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), Is.Null);
                Assert.That(typeof(ProgressBar).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), Is.Null);
            });

            context.Update(Time, Mouse(75, 10), new KeyboardState());
            context.Update(Time, Mouse(75, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(75, 10), new KeyboardState());

            Assert.That(slider.Value, Is.EqualTo(75));
            Assert.That(progress.Value, Is.EqualTo(35));
        }

        [Test]
        public void OptionButton_TracksGodotItemStateAndPresentsItsPopup()
        {
            var option = new OptionButton { Size = new Vector2(120, 28) };
            option.AddItem("Dark", 10);
            option.AddSeparator();
            option.AddItem("System", 20); option.SetItemDisabled(2, true);
            option.AddItem("Light", 30); option.SetItemMetadata(3, "light-theme"); option.SetItemTooltip(3, "Light editor theme");
            option.SetItemAutoTranslateMode(3, AutoTranslateMode.Disabled);
            option.SetFitToLongestItem(false);
            option.SetAllowReselect(true);
            option.SetSearchBarEnabled(true);
            option.SetSearchBarMinItemCount(2);
            option.SetSearchBarFuzzySearchEnabled(false);
            option.SetSearchBarFuzzySearchMaxMisses(1);
            var selected = 0; option.ItemSelected += (_, _) => selected++;
            option.Select(3);
            var context = new UIContext(); context.Add(option);
            option.ShowPopup(); var popupVisible = option.Popup.Visible;
            option.Popup.Hide(); option.GrabFocus();
            // Godot's OptionButton has no keyboard input handling of its own (no gui_input override
            // anywhere in option_button.cpp) - arrow keys are a no-op, not an item-cycling shortcut.
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Up));

            Assert.That(popupVisible, Is.True);
            Assert.That(option.Selected, Is.EqualTo(3), "Arrow keys don't cycle selection in Godot, so the explicit Select(3) call above is unaffected.");
            Assert.That(option.SelectedId, Is.EqualTo(30));
            Assert.That(option.GetItemMetadata(3), Is.EqualTo("light-theme"));
            Assert.That(option.GetItemTooltip(3), Is.EqualTo("Light editor theme"));
            Assert.That(option.GetItemAutoTranslateMode(3), Is.EqualTo(AutoTranslateMode.Disabled));
            Assert.That(option.IsItemDisabled(2), Is.True);
            Assert.That(option.IsItemSeparator(1), Is.True);
            Assert.That(option.GetItemCount(), Is.EqualTo(4));
            Assert.That(option.HasSelectableItems(), Is.True);
            Assert.That(option.GetSelectableItem(), Is.EqualTo(0));
            Assert.That(option.GetSelectableItem(true), Is.EqualTo(3));
            Assert.That(option.IsFitToLongestItem(), Is.False);
            Assert.That(option.GetAllowReselect(), Is.True);
            Assert.That(option.IsSearchBarEnabled(), Is.True);
            Assert.That(option.GetSearchBarMinItemCount(), Is.EqualTo(2));
            Assert.That(option.IsSearchBarFuzzySearchEnabled(), Is.False);
            Assert.That(option.GetSearchBarFuzzySearchMaxMisses(), Is.EqualTo(1));
            Assert.That(option.Popup.GetItemAutoTranslateMode(3), Is.EqualTo(AutoTranslateMode.Disabled));
            Assert.That(selected, Is.EqualTo(0), "Select(3) without emitSignal:true (the default) never fires ItemSelected, matching Godot's select() calling _select(idx, false).");
            Assert.Throws<ArgumentOutOfRangeException>(() => option.SetItemAutoTranslateMode(0, (AutoTranslateMode)999));
            Assert.Throws<ArgumentOutOfRangeException>(() => option.SetSearchBarMinItemCount(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => option.SetSearchBarFuzzySearchMaxMisses(-1));
        }

        [Test]
        public void OptionButton_PopupItemsMirrorRadioSelectionAndMutationsLikeGodot()
        {
            var icon = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var option = new OptionButton();
            option.AddItem("A", 10);
            option.AddSeparator("Group");
            option.AddIconItem(icon, "B", 20);

            Assert.That(option.Popup.IsItemRadioCheckable(0), Is.True);
            Assert.That(option.Popup.IsItemChecked(0), Is.True);
            Assert.That(option.Popup.GetItemText(1), Is.EqualTo("Group"));
            Assert.That(option.Popup.IsItemRadioCheckable(2), Is.True);
            Assert.That(option.Popup.GetItemIcon(2), Is.SameAs(icon));

            option.SetItemText(2, "Bee");
            option.SetItemIcon(2, null);
            option.SetItemId(2, 30);
            option.SetItemMetadata(2, "metadata");
            option.SetItemTooltip(2, "tooltip");
            option.Select(2);

            Assert.That(option.Popup.GetItemText(2), Is.EqualTo("Bee"));
            Assert.That(option.Popup.GetItemIcon(2), Is.Null);
            Assert.That(option.Popup.GetItemId(2), Is.EqualTo(30));
            Assert.That(option.Popup.GetItemMetadata(2), Is.EqualTo("metadata"));
            Assert.That(option.Popup.GetItemTooltip(2), Is.EqualTo("tooltip"));
            Assert.That(option.Popup.IsItemChecked(0), Is.False);
            Assert.That(option.Popup.IsItemChecked(2), Is.True);

            option.Select(-1);
            Assert.That(option.Popup.IsItemChecked(0), Is.False);
            Assert.That(option.Popup.IsItemChecked(2), Is.False);
        }

        [Test]
        public void OptionButton_MapsGodotShortcutInputActivatingItemsWithoutOpeningThePopup()
        {
            var option = new OptionButton { Size = new Vector2(120, 28) };
            option.AddItem("Dark", 10);
            option.AddItem("Light", 20);
            option.Popup.SetItemAccelerator(1, new PopupMenuShortcut("Light", Keys.L, control: true));
            var selected = -1; option.ItemSelected += (_, index) => selected = index;
            var context = new UIContext(); context.Add(option);
            context.Update(Time, Mouse(0, 0), new KeyboardState());

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.L));
            Assert.That(option.Selected, Is.EqualTo(1), "Godot's OptionButton::shortcut_input activates the accelerator-matched item directly.");
            Assert.That(selected, Is.EqualTo(1));
            Assert.That(option.Popup.Visible, Is.False, "The item is activated without ever opening the popup.");

            option.Select(0);
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            option.DisableShortcuts = true;
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.L));
            Assert.That(option.Selected, Is.EqualTo(0), "disable_shortcuts gates OptionButton's own shortcut_input item activation.");

            option.DisableShortcuts = false;
            option.SetItemDisabled(1, true);
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.L));
            Assert.That(option.Selected, Is.EqualTo(0), "A disabled item's accelerator is skipped by activate_item_by_event.");
        }

        [Test]
        public void OptionButton_SelectBypassesSelectabilityAndAddItemChecksSelectableItemsBeforeAdding()
        {
            var option = new OptionButton();
            option.AddItem("A"); option.AddItem("B"); option.AddItem("C");
            Assert.That(option.Selected, Is.EqualTo(0), "The first added selectable item auto-selects.");

            // Godot's OptionButton::_select never checks selectability - only the popup's own UI prevents
            // clicking a disabled item, but a direct select() call can target one.
            option.SetItemDisabled(1, true);
            option.Select(1);
            Assert.That(option.Selected, Is.EqualTo(1));

            // Godot's add_item/add_icon_item check !has_selectable_items() BEFORE adding, not whether
            // Selected is currently -1: with every existing item now disabled, Selected still points at
            // (disabled) index 1, but the new item must still auto-select since nothing else is selectable.
            option.SetItemDisabled(0, true); option.SetItemDisabled(2, true);
            var index = option.AddItem("D");
            Assert.That(option.Selected, Is.EqualTo(index));
        }

        [Test]
        public void OptionButton_RemoveItemDoesNotShiftSelectedLikeGodotsRealQuirk()
        {
            var option = new OptionButton();
            option.AddItem("A"); option.AddItem("B"); option.AddItem("C");
            option.Select(2);
            option.RemoveItem(0);
            // Godot's remove_item does NOT shift `current` down for later indices - Selected stays at 2
            // even though the item list has shrunk to 2 entries (a real Godot quirk, matched here for
            // behavioral parity), and the now out-of-range read gracefully falls back to -1/null instead
            // of throwing, like Godot's own ERR_FAIL_INDEX-guarded item getters.
            Assert.That(option.Selected, Is.EqualTo(2));
            Assert.That(option.SelectedId, Is.EqualTo(-1));
            Assert.That(option.SelectedMetadata, Is.Null);
        }

        [Test]
        public void OptionButton_ClosesAnAlreadyOpenPopupOnASecondPressLikeGodot()
        {
            var option = new OptionButton { Size = new Vector2(120, 28) };
            option.AddItem("A"); option.AddItem("B");
            var context = new UIContext(); context.Add(option);

            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            Assert.That(option.Popup.Visible, Is.True, "The first press opens the popup.");

            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            Assert.That(option.Popup.Visible, Is.False, "Godot's OptionButton::pressed() closes an already-open popup instead of reopening it.");
        }

        [Test]
        public void OptionButton_ShowPopupMatchesGodotFocusDirectionAndViewportGeometry()
        {
            var option = new OptionButton { Position = new Vector2(20, 30), Size = new Vector2(120, 28), LayoutDirection = LayoutDirection.RightToLeft };
            option.AddItem("Disabled"); option.SetItemDisabled(0, true);
            option.AddItem("Selected"); option.Select(1);
            var context = new UIContext { ViewportSize = new Vector2(400, 300) }; context.Add(option);

            option.ShowPopup();
            Assert.That(option.Popup.GetFocusedItem(), Is.EqualTo(1));
            Assert.That(option.Popup.LayoutDirection, Is.EqualTo(LayoutDirection.RightToLeft));
            Assert.That(option.Popup.Position, Is.EqualTo(new Vector2(20, 58)));
            Assert.That(option.Popup.Size.X, Is.GreaterThanOrEqualTo(option.Size.X));

            option.Popup.Hide();
            option.Popup.SetFocusedItem(-1);
            context.Update(Time, Mouse(25, 35, ButtonState.Pressed), new KeyboardState());
            Assert.That(option.Popup.Visible, Is.True);
            Assert.That(option.Popup.GetFocusedItem(), Is.EqualTo(-1), "Mouse opening scrolls to the selected item without assigning keyboard focus.");

            option.Popup.Hide();
            option.Position = new Vector2(130, 80);
            option.Size = new Vector2(60, 20);
            context.ViewportSize = new Vector2(160, 100);
            option.ShowPopup();
            Assert.That(option.Popup.Bounds.Left, Is.GreaterThanOrEqualTo(0));
            Assert.That(option.Popup.Bounds.Top, Is.GreaterThanOrEqualTo(0));
            Assert.That(option.Popup.Bounds.Right, Is.LessThanOrEqualTo(160));
            Assert.That(option.Popup.Bounds.Bottom, Is.LessThanOrEqualTo(100));
        }

        [Test]
        public void OptionButton_LongPopupScrollsTheRequestedItemIntoItsViewportLikeGodot()
        {
            var option = new OptionButton { Size = new Vector2(100, 20) };
            for (var index = 0; index < 12; index++) option.AddItem($"Item {index}");
            var context = new UIContext { ViewportSize = new Vector2(160, 100) };
            context.Add(option);

            option.ShowPopup();

            Assert.That(option.Popup.Bounds.Bottom, Is.LessThanOrEqualTo(100));
            var firstVisiblePoint = new Point(option.Popup.Bounds.Center.X, option.Popup.Bounds.Top + (int)(option.Popup.ItemHeight / 2) + 1);
            Assert.That(option.Popup.ItemAt(firstVisiblePoint), Is.EqualTo(0));
            context.Update(Time, Mouse(firstVisiblePoint.X, firstVisiblePoint.Y, scrollWheel: -120), new KeyboardState());
            Assert.That(option.Popup.ItemAt(firstVisiblePoint), Is.EqualTo(3), "Wheel scrolling must move the retained item viewport by three rows.");

            option.Popup.ScrollToItem(11);
            var lastItemPoint = new Point(option.Popup.Bounds.Center.X, option.Popup.Bounds.Bottom - (int)(option.Popup.ItemHeight / 2) - 1);
            context.Update(Time, Mouse(lastItemPoint.X, lastItemPoint.Y, ButtonState.Pressed, scrollWheel: -120), new KeyboardState());
            context.Update(Time, Mouse(lastItemPoint.X, lastItemPoint.Y, scrollWheel: -120), new KeyboardState());

            Assert.That(option.Selected, Is.EqualTo(11));
        }

        [Test]
        public void OptionButton_ConstructorMatchesGodotDefaultsAndResetsPressedOnPopupHide()
        {
            var button = new OptionButton();
            Assert.That(button.ToggleMode, Is.True, "Godot's OptionButton constructor calls set_toggle_mode(true).");
            Assert.That(button.TextAlignment, Is.EqualTo(HorizontalAlignment.Left), "Godot's constructor calls set_text_alignment(LEFT).");
            Assert.That(button.ActionMode, Is.EqualTo(ButtonActionMode.Press), "Godot's constructor calls set_action_mode(BUTTON_PRESS).");

            var context = new UIContext(); context.Add(button);
            button.ShowPopup();
            button.SetPressedDirect(true, false);
            button.Popup.Hide();
            Assert.That(button.ButtonPressed, Is.False, "The popup_hide connection must reset the pressed look, matching MenuButton's own established pattern.");
        }

        [Test]
        public void OptionButton_ShowsTheSelectedItemsIconAndFactorsIconWidthIntoFitToLongestItemLikeGodot()
        {
            var icon = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var button = new OptionButton { Font = CreateTestFont() };
            button.AddItem("A");
            button.AddIconItem(icon, "B");

            button.Select(0);
            Assert.That(button.Icon, Is.Null);

            button.Select(1);
            Assert.That(button.Icon, Is.SameAs(icon), "Select must show the newly-selected item's icon, matching Godot's _select calling set_button_icon.");

            var withIcon = button.GetMinimumSize();
            button.Clear();
            Assert.That(button.Icon, Is.Null, "Clear must also drop the shown icon, matching Godot's _select(NONE_SELECTED) branch.");
            button.AddItem("B");
            var withoutIcon = button.GetMinimumSize();
            Assert.That(withIcon.X, Is.GreaterThan(withoutIcon.X), "FitToLongestItem must factor in each item's own icon width, not just text width.");
        }

        [Test]
        public void OptionButton_CustomMinimumSizeIsAFloorRatherThanAddedToArrow()
        {
            var texture = CreateHeadlessTexture(8, 8);
            var theme = new Theme();
            theme.SetIcon("arrow", new ThemeIcon(texture, new Rectangle(0, 0, 8, 8), new Point(8, 8)), nameof(OptionButton));
            var context = new UIContext { Theme = theme };
            var button = new OptionButton { CustomMinimumSize = new Vector2(360, 38) };
            context.Add(button);

            Assert.That(button.GetMinimumSize(), Is.EqualTo(new Vector2(360, 38)));
        }

        [Test]
        public void OptionButton_FiresItemFocusedFromThePopupsIndexFocusedLikeGodot()
        {
            var button = new OptionButton();
            button.AddItem("A"); button.AddItem("B");
            var focused = -1;
            button.ItemFocused += (_, index) => focused = index;

            button.Popup.SetFocusedItem(1);

            Assert.That(focused, Is.EqualTo(1), "Godot wires the popup's id_focused straight through to OptionButton's item_focused signal.");
        }

        [Test]
        public void TextureButton_ClickMaskScalesToItsOwnSizeNotTheTextureSizeLikeGodot()
        {
            var texture = CreateHeadlessTexture(100, 100);
            var mask = new TextureButtonClickMask(10, 10);
            mask[9, 0] = true;
            var button = new TextureButton { Size = new Vector2(100, 100), StretchMode = TextureButtonStretchMode.Keep, TextureNormal = texture, ClickMask = mask };

            // Keep stretch mode with a 100x100 texture matching the 100x100 control means the
            // destination rect is exactly the control's bounds; the 10x10 mask must scale to ITS OWN
            // size (10x), matching Godot's has_point, not the 100x100 texture's (which would produce a
            // permanently out-of-range mask lookup for anything but the top-left corner).
            Assert.That(button.ContainsPoint(new Point(95, 5)), Is.True, "The click mask must scale to its own size, matching Godot's has_point.");
            Assert.That(button.ContainsPoint(new Point(5, 5)), Is.False);
        }

        [Test]
        public void TextureButton_HoverFallsBackToNormalWhenPressedTextureIsMissingLikeGodot()
        {
            var normal = CreateHeadlessTexture(10, 10);
            var button = new TextureButton { TextureNormal = normal };
            button.SetPressedDirect(true, false);
            button.PointerEntered();

            Assert.That(button.GetCurrentTexture(), Is.SameAs(normal), "Hovering with no hover/pressed texture must fall back to Normal, matching Godot's DRAW_HOVER case falling through when pressed is invalid.");
        }

        [Test]
        public void TextureButton_FocusOverlayReusesThePrimaryTexturesDestinationRectLikeGodot()
        {
            var normal = CreateHeadlessTexture(200, 100);
            var focused = CreateHeadlessTexture(50, 50);
            var button = new TextureButton { TextureNormal = normal, TextureFocused = focused, Size = new Vector2(200, 100), StretchMode = TextureButtonStretchMode.KeepCentered };

            var primaryLayout = button.GetTextureLayout(new Vector2(200, 100));
            var focusLayout = button.GetFocusOverlayLayout();

            Assert.That(focusLayout, Is.Not.Null);
            Assert.That(focusLayout.Value.Destination, Is.EqualTo(primaryLayout.Destination), "The focus overlay must reuse the primary texture's own destination rect, not compute fresh geometry from its own size.");
            Assert.That(focusLayout.Value.Source, Is.EqualTo(new Rectangle(0, 0, 50, 50)), "The focus overlay always samples its own full texture image though, never the primary's (possibly cropped) source region.");
        }

        [Test]
        public void GridContainer_UsesPerTrackMinimumsExpansionAndShrinkAlignment()
        {
            var grid = new GridContainer { Columns = 2, Size = new Vector2(100, 60), HorizontalSeparation = 4, VerticalSeparation = 4 };
            var left = new Control { CustomMinimumSize = new Vector2(20, 10), HorizontalSizeFlags = SizeFlags.ShrinkBegin, VerticalSizeFlags = SizeFlags.ShrinkBegin };
            var right = new Control { CustomMinimumSize = new Vector2(30, 10), HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand, VerticalSizeFlags = SizeFlags.Fill };
            var bottom = new Control { CustomMinimumSize = new Vector2(20, 10), HorizontalSizeFlags = SizeFlags.Fill, VerticalSizeFlags = SizeFlags.Fill | SizeFlags.Expand };
            grid.AddChild(left); grid.AddChild(right); grid.AddChild(bottom);
            var context = new UIContext(); context.Add(grid); context.Layout();

            Assert.That(grid.GetMinimumSize(), Is.EqualTo(new Vector2(54, 24)));
            Assert.That(left.Bounds, Is.EqualTo(new Rectangle(0, 0, 20, 10)));
            Assert.That(right.Bounds, Is.EqualTo(new Rectangle(24, 0, 76, 10)));
            Assert.That(bottom.Bounds, Is.EqualTo(new Rectangle(0, 14, 20, 46)));
        }

        [Test]
        public void Control_UniversalBoxPropertiesConstrainDesiredSizeAcrossPanels()
        {
            var control = new Control
            {
                Width = 80,
                AspectRatio = 2,
                MinWidth = 90,
                MaxWidth = 85,
                MinHeight = 20,
                MaxHeight = 50,
                Margin = new Thickness(3, 4, 5, 6),
            };

            Assert.Multiple(() =>
            {
                Assert.That(control.GetBoundDesiredSize(), Is.EqualTo(new Vector2(90, 40)));
                Assert.That(control.Margins, Is.EqualTo(new Thickness(3, 4, 5, 6)));
                Assert.Throws<ArgumentOutOfRangeException>(() => control.Width = -1);
                Assert.Throws<ArgumentOutOfRangeException>(() => control.MaxHeight = float.NaN);
                Assert.Throws<ArgumentOutOfRangeException>(() => control.AspectRatio = 0);
            });

            control.MinWidth = 0;
            var stack = new StackPanel();
            stack.AddChild(control);
            Assert.That(stack.GetMinimumSize(), Is.EqualTo(new Vector2(88, 50)));

            stack.RemoveChild(control);
            var overlay = new OverlayPanel();
            overlay.AddChild(control);
            Assert.That(overlay.GetMinimumSize(), Is.EqualTo(new Vector2(88, 50)));
        }

        [Test]
        public void StackPanel_ArrangesIntrinsicChildrenWithGapCrossAlignmentAndRtlOrder()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Gap = 4,
                CrossAxisAlignment = CrossAxisAlignment.Center,
                Size = new Vector2(100, 30),
            };
            var first = new Control { CustomMinimumSize = new Vector2(20, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(30, 20) };
            panel.AddChild(first);
            panel.AddChild(second);
            var context = new UIContext();
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(panel.GetMinimumSize(), Is.EqualTo(new Vector2(54, 20)));
                Assert.That(first.Bounds, Is.EqualTo(new Rectangle(0, 10, 20, 10)));
                Assert.That(second.Bounds, Is.EqualTo(new Rectangle(24, 5, 30, 20)));
            });

            panel.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.Multiple(() =>
            {
                Assert.That(first.Bounds.X, Is.EqualTo(80));
                Assert.That(second.Bounds.X, Is.EqualTo(46));
            });
        }

        [Test]
        public void CanvasAndOverlayPanels_ApplyAttachedPlacementMarginsAndRtlAlignment()
        {
            var canvas = new CanvasPanel { Size = new Vector2(100, 60) };
            var stretched = new Control { CustomMinimumSize = new Vector2(10, 10), Margins = new Thickness(2) };
            CanvasPanel.SetLeft(stretched, 10);
            CanvasPanel.SetRight(stretched, 20);
            CanvasPanel.SetTop(stretched, 5);
            canvas.AddChild(stretched);
            var overlay = new OverlayPanel { Size = new Vector2(100, 60) };
            var aligned = new Control { CustomMinimumSize = new Vector2(20, 10), Margins = new Thickness(2) };
            OverlayPanel.SetHorizontalAlignment(aligned, HorizontalAlignment.Left);
            OverlayPanel.SetVerticalAlignment(aligned, VerticalAlignment.Bottom);
            overlay.AddChild(aligned);
            var context = new UIContext();
            context.Add(canvas);
            context.Add(overlay);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(stretched.Bounds, Is.EqualTo(new Rectangle(12, 7, 66, 10)));
                Assert.That(aligned.Bounds, Is.EqualTo(new Rectangle(2, 48, 20, 10)));
                Assert.That(overlay.GetMinimumSize(), Is.EqualTo(new Vector2(24, 14)));
            });

            overlay.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.That(aligned.Bounds.X, Is.EqualTo(78));
        }

        [Test]
        public void WrapPanel_UsesIndependentGapsMarginsCrossAlignmentAndRtlMirroring()
        {
            var panel = new WrapPanel
            {
                Size = new Vector2(55, 60),
                ItemGap = 5,
                LineGap = 7,
                CrossAxisAlignment = CrossAxisAlignment.Center,
            };
            var first = new Control { CustomMinimumSize = new Vector2(20, 10), Margins = new Thickness(1) };
            var second = new Control { CustomMinimumSize = new Vector2(20, 20), Margins = new Thickness(1) };
            var third = new Control { CustomMinimumSize = new Vector2(30, 10) };
            panel.AddChild(first);
            panel.AddChild(second);
            panel.AddChild(third);
            var context = new UIContext();
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(panel.LineCount, Is.EqualTo(2));
                Assert.That(first.Bounds, Is.EqualTo(new Rectangle(1, 6, 20, 10)));
                Assert.That(second.Bounds, Is.EqualTo(new Rectangle(28, 1, 20, 20)));
                Assert.That(third.Bounds, Is.EqualTo(new Rectangle(0, 29, 30, 10)));
                Assert.That(panel.GetMinimumSize(), Is.EqualTo(new Vector2(30, 39)));
            });

            panel.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.Multiple(() =>
            {
                Assert.That(first.Bounds.X, Is.EqualTo(34));
                Assert.That(second.Bounds.X, Is.EqualTo(7));
                Assert.That(third.Bounds.X, Is.EqualTo(25));
            });
        }

        [Test]
        public void FoundationalLayoutLengths_AreTypedValidatedAndUseContentForIndefinitePercentages()
        {
            Assert.Multiple(() =>
            {
                Assert.That(LayoutLength.Pixels(12).Resolve(100, 7), Is.EqualTo(12));
                Assert.That(LayoutLength.Percent(.25f).Resolve(100, 7), Is.EqualTo(25));
                Assert.That(LayoutLength.Percent(.25f).Resolve(float.PositiveInfinity, 7), Is.EqualTo(7));
                Assert.That(LayoutLength.Auto.Resolve(100, 7), Is.EqualTo(7));
                Assert.That(GridTrackSize.MinMax(LayoutLength.Pixels(10), LayoutLength.Percent(.5f)), Is.EqualTo(new GridTrackSizeValueSource().Value));
                Assert.That(() => GridTrackSize.Star(0), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => LayoutLength.Pixels(float.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        private sealed class GridTrackSizeValueSource
        {
            public GridTrackSize Value => GridTrackSize.MinMax(LayoutLength.Pixels(10), LayoutLength.Percent(.5f));
        }

        [Test]
        public void FlexPanel_AppliesOrderGrowJustificationReverseAndRtl()
        {
            var panel = new FlexPanel { Size = new Vector2(100, 30), ColumnGap = 4, AlignItems = FlexAlign.Center };
            var first = new Control { CustomMinimumSize = new Vector2(20, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(20, 20) };
            FlexPanel.SetOrder(first, 1);
            FlexPanel.SetGrow(first, 1);
            panel.AddChild(first);
            panel.AddChild(second);
            var context = new UIContext();
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(second.Bounds, Is.EqualTo(new Rectangle(0, 5, 20, 20)));
                Assert.That(first.Bounds, Is.EqualTo(new Rectangle(24, 10, 76, 10)));
            });

            panel.Direction = FlexDirection.RowReverse;
            context.Layout();
            Assert.That(second.Bounds.X, Is.EqualTo(80));
            panel.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.That(second.Bounds.X, Is.EqualTo(0));
        }

        [Test]
        public void FlexPanel_MirrorsColumnCrossAlignmentAndKeepsEqualOrderStable()
        {
            var panel = new FlexPanel
            {
                Direction = FlexDirection.Column,
                AlignItems = FlexAlign.Start,
                LayoutDirection = LayoutDirection.RightToLeft,
                Size = new Vector2(30, 80),
            };
            var children = new List<Control>();
            for (var index = 0; index < 32; index++)
            {
                var child = new Control { CustomMinimumSize = new Vector2(10, 2) };
                FlexPanel.SetOrder(child, index % 2);
                children.Add(child);
                panel.AddChild(child);
            }
            var context = new UIContext();
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(children[0].Bounds.X, Is.EqualTo(20));
                for (var index = 0; index < 16; index++)
                {
                    Assert.That(children[index * 2].Bounds.Y, Is.EqualTo(index * 2));
                    Assert.That(children[index * 2 + 1].Bounds.Y, Is.EqualTo(32 + index * 2));
                }
            });
        }

        [Test]
        public void GridPanel_ResolvesExplicitTracksSpansAlignmentGapsAndRtl()
        {
            var panel = new GridPanel { Size = new Vector2(120, 50), ColumnGap = 5, RowGap = 4 };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Pixels(20) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Star() });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridTrackSize.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridTrackSize.Star() });
            var fixedChild = new Control { CustomMinimumSize = new Vector2(10, 10) };
            var aligned = new Control { CustomMinimumSize = new Vector2(15, 8) };
            GridPanel.SetColumn(aligned, 2);
            GridPanel.SetHorizontalAlignment(aligned, HorizontalAlignment.Right);
            GridPanel.SetVerticalAlignment(aligned, VerticalAlignment.Center);
            var spanning = new Control { CustomMinimumSize = new Vector2(50, 12) };
            GridPanel.SetRow(spanning, 1);
            GridPanel.SetColumnSpan(spanning, 2);
            panel.AddChild(fixedChild);
            panel.AddChild(aligned);
            panel.AddChild(spanning);
            var context = new UIContext();
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(fixedChild.Bounds, Is.EqualTo(new Rectangle(0, 0, 20, 10)));
                Assert.That(aligned.Bounds, Is.EqualTo(new Rectangle(105, 1, 15, 8)));
                Assert.That(spanning.Bounds, Is.EqualTo(new Rectangle(0, 14, 100, 36)));
            });

            panel.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.Multiple(() =>
            {
                Assert.That(fixedChild.Bounds.X, Is.EqualTo(100));
                Assert.That(aligned.Bounds.X, Is.EqualTo(0));
            });
        }

        [Test]
        public void GridPanel_MinMaxStarDefinitionMutationAndHighScaleStayDeterministic()
        {
            var flexible = new ColumnDefinition { Width = GridTrackSize.MinMax(LayoutLength.Pixels(20), LayoutLength.Star()) };
            var panel = new GridPanel { Size = new Vector2(50.5f, 20), ColumnGap = .5f };
            panel.ColumnDefinitions.Add(flexible);
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Pixels(10) });
            var first = new Control { CustomMinimumSize = new Vector2(5, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(5, 10) };
            GridPanel.SetColumn(second, 1);
            panel.AddChild(first);
            panel.AddChild(second);
            var context = new UIContext { DisplayScale = 2 };
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(first.Size.X, Is.EqualTo(40));
                Assert.That(second.Position.X, Is.EqualTo(40.5f));
            });

            flexible.Width = GridTrackSize.Pixels(25);
            context.Layout();
            Assert.Multiple(() =>
            {
                Assert.That(first.Size.X, Is.EqualTo(25));
                Assert.That(second.Position.X, Is.EqualTo(25.5f));
            });
        }

        [Test]
        public void CanvasPanel_AnchorPositionDoesNotDriftAcrossLayouts()
        {
            var panel = new CanvasPanel { Size = new Vector2(100, 100) };
            var child = new Control { Position = new Vector2(50, 50), CustomMinimumSize = new Vector2(20, 20) };
            var movedBeforeLayout = new Control { Position = new Vector2(50, 50), CustomMinimumSize = new Vector2(20, 20) };
            CanvasPanel.SetAnchor(child, new Vector2(.5f));
            CanvasPanel.SetAnchor(movedBeforeLayout, new Vector2(.5f));
            movedBeforeLayout.Position = new Vector2(70, 70);
            panel.AddChild(child);
            panel.AddChild(movedBeforeLayout);
            var context = new UIContext();
            context.Add(panel);
            context.Layout();
            Assert.That(child.Position, Is.EqualTo(new Vector2(40, 40)));
            Assert.That(movedBeforeLayout.Position, Is.EqualTo(new Vector2(60, 60)));

            panel.QueueLayout();
            context.Layout();
            Assert.That(child.Position, Is.EqualTo(new Vector2(40, 40)));

            child.Position = new Vector2(70, 70);
            context.Layout();
            Assert.That(child.Position, Is.EqualTo(new Vector2(60, 60)));
        }

        [Test]
        public void FlexPanel_FreezesMinimumAndMaximumThenRedistributesRemainingSpace()
        {
            var shrinking = new FlexPanel { Size = new Vector2(50, 20) };
            var firstMinimum = new Control { CustomMinimumSize = new Vector2(40, 10) };
            var secondMinimum = new Control { CustomMinimumSize = new Vector2(40, 10) };
            shrinking.AddChild(firstMinimum);
            shrinking.AddChild(secondMinimum);
            var growing = new FlexPanel { Size = new Vector2(100, 20) };
            var capped = new Control { CustomMinimumSize = new Vector2(20, 10), CustomMaximumSize = new Vector2(30, -1) };
            var uncapped = new Control { CustomMinimumSize = new Vector2(20, 10) };
            FlexPanel.SetGrow(capped, 1);
            FlexPanel.SetGrow(uncapped, 1);
            growing.AddChild(capped);
            growing.AddChild(uncapped);
            var context = new UIContext();
            context.Add(shrinking);
            context.Add(growing);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(firstMinimum.Size.X, Is.EqualTo(40));
                Assert.That(secondMinimum.Size.X, Is.EqualTo(40));
                Assert.That(capped.Size.X, Is.EqualTo(30));
                Assert.That(uncapped.Size.X, Is.EqualTo(70));
                Assert.That(() => FlexPanel.SetBasis(capped, LayoutLength.Star()), Throws.TypeOf<ArgumentException>());
            });
        }

        [Test]
        public void GridPanel_SpanningContentPreservesFixedTracksAndUsesFlexibleRemainder()
        {
            var panel = new GridPanel { Size = new Vector2(100, 20) };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Pixels(80) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Auto });
            var spanning = new Control { CustomMinimumSize = new Vector2(100, 10) };
            GridPanel.SetColumnSpan(spanning, 2);
            var marker = new Control { CustomMinimumSize = new Vector2(5, 10) };
            GridPanel.SetColumn(marker, 1);
            panel.AddChild(spanning);
            panel.AddChild(marker);
            var context = new UIContext();
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(spanning.Size.X, Is.EqualTo(100));
                Assert.That(marker.Position.X, Is.EqualTo(80));
                Assert.That(marker.Size.X, Is.EqualTo(20));
            });
        }

        [Test]
        public void LayoutConstraintsRejectInvalidUnitsAndWrapMinimumIsIntrinsic()
        {
            var child = new Control { CustomMinimumSize = new Vector2(20, 10) };
            var panel = new WrapPanel();
            panel.AddChild(child);

            Assert.Multiple(() =>
            {
                Assert.That(panel.GetMinimumSize(), Is.EqualTo(new Vector2(20, 10)));
                Assert.That(() => FlexPanel.SetBasis(child, LayoutLength.Star()), Throws.TypeOf<ArgumentException>());
                Assert.That(() => GridTrackSize.MinMax(LayoutLength.Star(), LayoutLength.Auto), Throws.TypeOf<ArgumentException>());
                Assert.That(() => GridTrackSize.MinMax(LayoutLength.Pixels(20), LayoutLength.Pixels(10)), Throws.TypeOf<ArgumentException>());
                Assert.That(GridTrackSize.MinMax(LayoutLength.Pixels(10), LayoutLength.Star()), Is.Not.EqualTo(default(GridTrackSize)));
            });
        }

        [Test]
        public void LayoutPanels_SnapFractionalEdgesAtDisplayScale()
        {
            var overlay = new OverlayPanel { Size = new Vector2(21, 20) };
            var centered = new Control { CustomMinimumSize = new Vector2(10, 10) };
            OverlayPanel.SetHorizontalAlignment(centered, HorizontalAlignment.Center);
            overlay.AddChild(centered);
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Size = new Vector2(20, 10) };
            var first = new Control { CustomMinimumSize = new Vector2(5.25f, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(5.25f, 10) };
            var third = new Control { CustomMinimumSize = new Vector2(5.25f, 10) };
            stack.AddChild(first);
            stack.AddChild(second);
            stack.AddChild(third);
            var context = new UIContext { DisplayScale = 2 };
            context.Add(overlay);
            context.Add(stack);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(centered.Position.X, Is.EqualTo(5.5f));
                Assert.That(first.Position.X + first.Size.X, Is.EqualTo(second.Position.X));
                Assert.That(second.Position.X + second.Size.X, Is.EqualTo(third.Position.X));
                Assert.That(first.Position.X * 2, Is.EqualTo(MathF.Round(first.Position.X * 2)));
                Assert.That(third.Size.X * 2, Is.EqualTo(MathF.Round(third.Size.X * 2)));
            });

            context.DisplayScale = 1;
            context.Layout();
            Assert.That(centered.Position.X, Is.EqualTo(6));
            context.DisplayScale = 2;
            context.Layout();
            Assert.That(centered.Position.X, Is.EqualTo(5.5f));
        }

        [Test]
        public void LayoutPanels_LayerAndClipConstrainedOverflow()
        {
            var overlay = new OverlayPanel { Size = new Vector2(30, 30) };
            var lower = new Button { CustomMinimumSize = new Vector2(30, 30) };
            var upper = new Button { CustomMinimumSize = new Vector2(30, 30) };
            OverlayPanel.SetZIndex(lower, 1);
            OverlayPanel.SetZIndex(upper, 2);
            overlay.AddChild(upper);
            overlay.AddChild(lower);
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Position = new Vector2(40, 0), Size = new Vector2(20, 10), ClipContents = false };
            var overflow = new Control { CustomMinimumSize = new Vector2(40, 10) };
            CanvasPanel.SetZIndex(overflow, 3);
            stack.AddChild(overflow);
            var context = new UIContext();
            context.Add(overlay);
            context.Add(stack);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(context.HitTest(new Point(5, 5)), Is.SameAs(upper));
                Assert.That(OverlayPanel.GetZIndex(upper), Is.EqualTo(2));
                Assert.That(CanvasPanel.GetZIndex(overflow), Is.EqualTo(3));
                Assert.That(overflow.Size.X, Is.EqualTo(40));
                Assert.That(context.HitTest(new Point(70, 5)), Is.SameAs(overflow));
            });

            stack.ClipContents = true;
            Assert.That(context.HitTest(new Point(70, 5)), Is.Not.SameAs(overflow));
        }

        [Test]
        public void GridPanel_PercentageTrackFallsBackToContentUntilConstraintIsDefinite()
        {
            var panel = new GridPanel();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Percent(.5f) });
            var child = new Control { CustomMinimumSize = new Vector2(20, 10) };
            panel.AddChild(child);

            Assert.That(panel.GetMinimumSize().X, Is.EqualTo(20));

            panel.Size = new Vector2(100, 10);
            var context = new UIContext();
            context.Add(panel);
            context.Layout();
            Assert.That(child.Size.X, Is.EqualTo(50));
        }

        [Test]
        public void GridPanel_FreezesConstrainedStarTracksAndOrdersSpansPerAxis()
        {
            var stars = new GridPanel { Size = new Vector2(100, 10) };
            stars.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Star() });
            stars.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Star() });
            var wide = new Control { CustomMinimumSize = new Vector2(80, 10) };
            var marker = new Control();
            GridPanel.SetColumn(marker, 1);
            stars.AddChild(wide);
            stars.AddChild(marker);

            var autos = new GridPanel();
            autos.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Auto });
            autos.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Auto });
            var spanning = new Control { CustomMinimumSize = new Vector2(100, 10) };
            GridPanel.SetColumnSpan(spanning, 2);
            var singleColumn = new Control { CustomMinimumSize = new Vector2(80, 10) };
            GridPanel.SetRowSpan(singleColumn, 3);
            autos.AddChild(spanning);
            autos.AddChild(singleColumn);

            var context = new UIContext();
            context.Add(stars);
            context.Layout();
            Assert.Multiple(() =>
            {
                Assert.That(wide.Size.X, Is.EqualTo(80));
                Assert.That(marker.Position.X, Is.EqualTo(80));
                Assert.That(marker.Size.X, Is.EqualTo(20));
                Assert.That(autos.GetMinimumSize().X, Is.EqualTo(100));
            });
        }

        [Test]
        public void FlexPanel_NoWrapIgnoresAlignContentAndWrapReverseMirrorsLines()
        {
            var singleLine = new FlexPanel
            {
                Size = new Vector2(100, 50),
                AlignItems = FlexAlign.Stretch,
                AlignContent = FlexAlignContent.Start,
            };
            var stretched = new Control { CustomMinimumSize = new Vector2(20, 10) };
            singleLine.AddChild(stretched);

            var reversed = new FlexPanel
            {
                Size = new Vector2(25, 100),
                Wrap = FlexWrap.WrapReverse,
                AlignContent = FlexAlignContent.Start,
            };
            var first = new Control { CustomMinimumSize = new Vector2(10, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(10, 10) };
            var third = new Control { CustomMinimumSize = new Vector2(10, 10) };
            reversed.AddChild(first);
            reversed.AddChild(second);
            reversed.AddChild(third);
            var context = new UIContext();
            context.Add(singleLine);
            context.Add(reversed);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(stretched.Size.Y, Is.EqualTo(50));
                Assert.That(first.Position.Y, Is.EqualTo(90));
                Assert.That(second.Position.Y, Is.EqualTo(90));
                Assert.That(third.Position.Y, Is.EqualTo(80));
            });
        }

        [Test]
        public void Control_ComposedVisualStateUsesTransformClipAndHitTestVisibilityForSubtrees()
        {
            var root = new Control
            {
                Position = new Vector2(10, 10),
                Size = new Vector2(20, 20),
                RenderTransform = new TranslateTransform { X = 30, Y = 5 },
                TransformOrigin = Vector2.Zero,
                Clip = new EllipseGeometry(),
                Foreground = Color.Lime,
                Language = "ar",
                FontSize = 18,
            };
            var child = new Control { Position = new Vector2(5, 5), Size = new Vector2(10, 10) };
            root.AddChild(child);
            var context = new UIContext();
            context.Add(root);
            BringIntoViewRequestedEventArgs request = null;
            root.BringIntoViewRequested += (_, value) => { request = value; value.Handled = true; };
            child.BringIntoView();

            Assert.Multiple(() =>
            {
                Assert.That(context.HitTest(new Point(50, 25)), Is.SameAs(child));
                Assert.That(context.HitTest(new Point(41, 16)), Is.Null, "The transformed point is inside bounds but outside the elliptical clip.");
                Assert.That(context.HitTest(new Point(20, 20)), Is.Null, "The untransformed location must not remain interactive.");
                Assert.That(root.VisualBounds, Is.EqualTo(new Rectangle(40, 15, 20, 20)));
                Assert.That(root.FocusBounds, Is.EqualTo(root.VisualBounds));
                Assert.That(root.AccessibilityBounds, Is.EqualTo(root.VisualBounds));
                Assert.That(child.VisualBounds, Is.EqualTo(new Rectangle(45, 20, 10, 10)));
                Assert.That(request?.Target, Is.SameAs(child));
                Assert.That(request?.TargetBounds, Is.EqualTo(child.VisualBounds));
                Assert.That(child.Foreground, Is.EqualTo(Color.Lime));
                Assert.That(child.Language, Is.EqualTo("ar"));
                Assert.That(child.FontSize, Is.EqualTo(18));
                Assert.Throws<ArgumentOutOfRangeException>(() => root.Opacity = 1.01f);
                Assert.That(typeof(DrawingElement).IsSubclassOf(typeof(Control)), Is.True);
                Assert.That(typeof(TemplatedControl).IsAssignableFrom(typeof(DrawingElement)), Is.False);
                Assert.That(typeof(DrawingContext).GetMethod(nameof(DrawingContext.MeasureText)), Is.Not.Null);
                Assert.That(typeof(DrawingContext).GetMethod(nameof(DrawingContext.DrawText)), Is.Not.Null);
                Assert.That(typeof(DrawingContext).GetMethod(nameof(DrawingContext.DrawImage)), Is.Not.Null);
                Assert.That(typeof(DrawingContext).GetMethod(nameof(DrawingContext.DrawIcon)), Is.Not.Null);
            });

            root.IsHitTestVisible = false;
            Assert.That(context.HitTest(new Point(50, 25)), Is.Null);
        }

        [Test]
        public void Control_VisibilityEffectiveStateAndPixelSnappingShareUniversalSemantics()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Size = new Vector2(20, 20) };
            var child = new Control
            {
                CustomMinimumSize = new Vector2(10.25f, 10),
                FocusMode = FocusMode.All,
                Visibility = Visibility.Hidden,
                PixelSnapping = PixelSnapping.Disabled,
            };
            panel.AddChild(child);
            var context = new UIContext { DisplayScale = 2 };
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(panel.GetMinimumSize().X, Is.EqualTo(10.25f));
                Assert.That(child.Size.X, Is.EqualTo(10.25f));
                Assert.That(context.HitTest(new Point(5, 5)), Is.SameAs(panel));
                Assert.That(child.IsEffectivelyEnabled, Is.True);
                Assert.That(child.EffectiveCursor, Is.EqualTo(Cursor.Arrow));
            });

            child.GrabFocus();
            Assert.That(context.FocusedControl, Is.Null);
            panel.Enabled = false;
            Assert.That(child.IsEffectivelyEnabled, Is.False);
            child.Visibility = Visibility.Collapsed;
            Assert.That(panel.GetMinimumSize(), Is.EqualTo(Vector2.Zero));
        }

        [Test]
        public void HorizontalRtlPanelsUseRightMarginAsLogicalStart()
        {
            var margins = new Thickness(2, 0, 8, 0);
            var stack = new StackPanel { Orientation = Orientation.Horizontal, LayoutDirection = LayoutDirection.RightToLeft, Size = new Vector2(100, 20) };
            var stackChild = new Control { CustomMinimumSize = new Vector2(20, 10), Margins = margins };
            stack.AddChild(stackChild);
            var wrap = new WrapPanel { LayoutDirection = LayoutDirection.RightToLeft, Size = new Vector2(100, 20) };
            var wrapChild = new Control { CustomMinimumSize = new Vector2(20, 10), Margins = margins };
            wrap.AddChild(wrapChild);
            var flex = new FlexPanel { LayoutDirection = LayoutDirection.RightToLeft, Size = new Vector2(100, 20) };
            var flexChild = new Control { CustomMinimumSize = new Vector2(20, 10), Margins = margins };
            flex.AddChild(flexChild);
            var verticalWrap = new WrapPanel { Orientation = Orientation.Vertical, LayoutDirection = LayoutDirection.RightToLeft, Size = new Vector2(100, 30) };
            var verticalWrapChild = new Control { CustomMinimumSize = new Vector2(20, 10), Margins = margins };
            verticalWrap.AddChild(verticalWrapChild);
            var columnFlex = new FlexPanel { Direction = FlexDirection.Column, AlignItems = FlexAlign.Start, LayoutDirection = LayoutDirection.RightToLeft, Size = new Vector2(100, 30) };
            var columnFlexChild = new Control { CustomMinimumSize = new Vector2(20, 10), Margins = margins };
            columnFlex.AddChild(columnFlexChild);
            var context = new UIContext();
            context.Add(stack);
            context.Add(wrap);
            context.Add(flex);
            context.Add(verticalWrap);
            context.Add(columnFlex);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(stackChild.Position.X, Is.EqualTo(72));
                Assert.That(wrapChild.Position.X, Is.EqualTo(72));
                Assert.That(flexChild.Position.X, Is.EqualTo(72));
                Assert.That(verticalWrapChild.Position.X, Is.EqualTo(72));
                Assert.That(columnFlexChild.Position.X, Is.EqualTo(72));
            });
        }

        [Test]
        public void FlexPanel_WrapUsesMinMaxConstrainedHypotheticalSizes()
        {
            var panel = new FlexPanel { Wrap = FlexWrap.Wrap, AlignContent = FlexAlignContent.Start, Size = new Vector2(50, 30) };
            var first = new Control { CustomMinimumSize = new Vector2(40, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(40, 10) };
            FlexPanel.SetBasis(first, LayoutLength.Pixels(0));
            FlexPanel.SetBasis(second, LayoutLength.Pixels(0));
            panel.AddChild(first);
            panel.AddChild(second);
            var context = new UIContext();
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(panel.GetMinimumSize().X, Is.EqualTo(40));
                Assert.That(first.Position.Y, Is.EqualTo(0));
                Assert.That(second.Position.Y, Is.EqualTo(10));
            });
        }

        [Test]
        public void GridPanel_SpanRedistributesPastCappedFitContentTrack()
        {
            var panel = new GridPanel { Size = new Vector2(100, 10) };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.FitContent(20) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Auto });
            var spanning = new Control { CustomMinimumSize = new Vector2(100, 10) };
            GridPanel.SetColumnSpan(spanning, 2);
            var marker = new Control();
            GridPanel.SetColumn(marker, 1);
            panel.AddChild(spanning);
            panel.AddChild(marker);
            var context = new UIContext();
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(spanning.Size.X, Is.EqualTo(100));
                Assert.That(marker.Position.X, Is.EqualTo(20));
                Assert.That(marker.Size.X, Is.EqualTo(80));
            });
        }

        [Test]
        public void FlexPanel_ReverseAxesPreserveAsymmetricPhysicalMargins()
        {
            var rowReverse = new FlexPanel { Direction = FlexDirection.RowReverse, Size = new Vector2(100, 30) };
            var rowChild = new Control { CustomMinimumSize = new Vector2(20, 10), Margins = new Thickness(2, 0, 8, 0) };
            rowReverse.AddChild(rowChild);
            var columnReverse = new FlexPanel { Direction = FlexDirection.ColumnReverse, Size = new Vector2(30, 100) };
            var columnChild = new Control { CustomMinimumSize = new Vector2(10, 20), Margins = new Thickness(0, 2, 0, 8) };
            columnReverse.AddChild(columnChild);
            var wrapReverse = new FlexPanel { Wrap = FlexWrap.WrapReverse, AlignContent = FlexAlignContent.Start, Size = new Vector2(30, 100) };
            var wrapChild = new Control { CustomMinimumSize = new Vector2(10, 20), Margins = new Thickness(0, 2, 0, 8) };
            wrapReverse.AddChild(wrapChild);
            var context = new UIContext();
            context.Add(rowReverse);
            context.Add(columnReverse);
            context.Add(wrapReverse);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(rowChild.Position.X, Is.EqualTo(72));
                Assert.That(columnChild.Position.Y, Is.EqualTo(72));
                Assert.That(wrapChild.Position.Y, Is.EqualTo(72));
            });
        }

        [Test]
        public void GridContainer_MirrorsColumnOrderUnderRtlLikeGodot()
        {
            // Sized to exactly fit both columns plus the gap, so GridContainer's "no explicit Expand
            // flag anywhere" fallback (which spreads leftover space across every column) never engages.
            var grid = new GridContainer { Columns = 2, Size = new Vector2(84, 20), HorizontalSeparation = 4 };
            var first = new Control { CustomMinimumSize = new Vector2(40, 20) };
            var second = new Control { CustomMinimumSize = new Vector2(40, 20) };
            grid.AddChild(first); grid.AddChild(second);
            var context = new UIContext(); context.Add(grid); context.Layout();

            Assert.That(first.Bounds.X, Is.EqualTo(0));
            Assert.That(second.Bounds.X, Is.EqualTo(44));

            grid.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.That(first.Bounds.X, Is.EqualTo(44), "Godot's GridContainer lays RTL columns out from the right edge inward.");
            Assert.That(second.Bounds.X, Is.EqualTo(0));
        }

        [Test]
        public void GridContainer_ExpandedColumnsShareAnEqualSizeWithStarvationLikeGodotResort()
        {
            // Godot's GridContainer::_resort gives every expanded column the SAME final size
            // (remaining_space / expanded_count), not its own minimum plus an equal share of the
            // extra. A column whose minimum exceeds that equal share is pinned to its own minimum and
            // dropped from the pool, and the remaining space is redivided among what's left.
            var grid = new GridContainer { Columns = 3, Size = new Vector2(100, 20), HorizontalSeparation = 0 };
            var fixedCol = new Control { CustomMinimumSize = new Vector2(10, 10) };
            var smallExpand = new Control { CustomMinimumSize = new Vector2(10, 10), HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand };
            var largeExpand = new Control { CustomMinimumSize = new Vector2(70, 10), HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand };
            grid.AddChild(fixedCol); grid.AddChild(smallExpand); grid.AddChild(largeExpand);
            var context = new UIContext(); context.Add(grid); context.Layout();

            Assert.That(fixedCol.Bounds.X, Is.EqualTo(0));
            Assert.That(fixedCol.Bounds.Width, Is.EqualTo(10));
            Assert.That(largeExpand.Bounds.Width, Is.EqualTo(70), "Pinned to its own minimum after starving out of the equal-share pool.");
            Assert.That(smallExpand.Bounds.Width, Is.EqualTo(20), "Gets the entire remaining space once the larger column is pinned.");
            Assert.That(smallExpand.Bounds.X, Is.EqualTo(10));
            Assert.That(largeExpand.Bounds.X, Is.EqualTo(30));
        }

        [Test]
        public void SplitContainer_ClampsGodotOffsetToBothMinimumSizesAndSupportsCollapse()
        {
            var split = new HSplitContainer { Size = new Vector2(200, 40), DragAreaSize = 6, SplitOffset = 40 };
            var first = new Control { CustomMinimumSize = new Vector2(80, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(100, 10) };
            split.AddChild(first); split.AddChild(second);
            var context = new UIContext(); context.Add(split); context.Layout();

            Assert.That(split.GetMinimumSize(), Is.EqualTo(new Vector2(186, 10)));
            Assert.That(first.Bounds, Is.EqualTo(new Rectangle(0, 0, 94, 40)));
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(100, 0, 100, 40)));
            Assert.That(split.ResolvedSplitOffset, Is.EqualTo(-3));
            // Godot's collapsed mode snaps the divider to the DEFAULT (evenly-stretched) position, not to
            // one extreme - here that default (97, before the min-size clamp) is already clamped down to
            // 94 by the same min-size constraint active before collapsing, so nothing further moves.
            split.Collapsed = true; context.Layout();
            Assert.That(first.Size.X, Is.EqualTo(94));
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(100, 0, 100, 40)));
        }

        [Test]
        public void SplitContainer_SwapsChildSidesUnderHorizontalRtlLikeGodotResort()
        {
            // Godot's SplitContainer::_resort swaps which child sits on which side under horizontal RTL,
            // on top of the divider position itself already being mirrored; vertical splits are unaffected.
            var split = new HSplitContainer { Size = new Vector2(200, 40), DragAreaSize = 6 };
            var first = new Control(); var second = new Control();
            split.AddChild(first); split.AddChild(second);
            var context = new UIContext(); context.Add(split); context.Layout();
            Assert.That(first.Bounds.X, Is.EqualTo(0));

            split.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.That(first.Bounds.X, Is.EqualTo(103), "Under RTL, the first child moves to the right side.");
            Assert.That(second.Bounds.X, Is.EqualTo(0), "...and the second child moves to the left side.");
        }

        [Test]
        public void SplitContainer_NudgesTheDraggerByTenPercentOnKeyboardInputLikeGodot()
        {
            var horizontal = new HSplitContainer { Size = new Vector2(200, 100) };
            horizontal.AddChild(new Control()); horizontal.AddChild(new Control());
            var horizontalContext = new UIContext(); horizontalContext.Add(horizontal); horizontalContext.Layout();

            horizontal.KeyPressed(Keys.Right);
            Assert.That(horizontal.SplitOffset, Is.EqualTo(20), "Godot's ui_right nudges a horizontal split forward by 10% of its width in LTR.");
            horizontal.KeyPressed(Keys.Left);
            Assert.That(horizontal.SplitOffset, Is.EqualTo(0), "ui_left nudges it back by the same amount.");
            horizontal.KeyPressed(Keys.Up);
            Assert.That(horizontal.SplitOffset, Is.EqualTo(0), "Vertical keys are ignored by a horizontal split's dragger.");

            horizontal.LayoutDirection = LayoutDirection.RightToLeft;
            horizontal.KeyPressed(Keys.Right);
            Assert.That(horizontal.SplitOffset, Is.EqualTo(-20), "Godot swaps left/right nudge direction for a horizontal split under RTL.");
            horizontal.KeyPressed(Keys.Left);
            Assert.That(horizontal.SplitOffset, Is.EqualTo(0));

            var vertical = new VSplitContainer { Size = new Vector2(100, 200) };
            vertical.AddChild(new Control()); vertical.AddChild(new Control());
            var verticalContext = new UIContext(); verticalContext.Add(vertical); verticalContext.Layout();
            vertical.KeyPressed(Keys.Down);
            Assert.That(vertical.SplitOffset, Is.EqualTo(20), "Godot's ui_down nudges a vertical split forward by 10% of its height, unaffected by RTL.");
            vertical.KeyPressed(Keys.Up);
            Assert.That(vertical.SplitOffset, Is.EqualTo(0));

            vertical.DraggingEnabled = false;
            vertical.KeyPressed(Keys.Down);
            Assert.That(vertical.SplitOffset, Is.EqualTo(0), "Godot gates keyboard nudging on dragging_enabled.");
            vertical.DraggingEnabled = true;
            vertical.Collapsed = true;
            vertical.KeyPressed(Keys.Down);
            Assert.That(vertical.SplitOffset, Is.EqualTo(0), "Godot skips the dragger entirely while the split is collapsed.");
        }

        [Test]
        public void FlowContainer_AppliesLastWrapAlignmentAndTracksLineMetrics()
        {
            var flow = new HFlowContainer { Size = new Vector2(100, 40), Separation = 4, LastWrapAlignment = FlowLastWrapAlignment.Center };
            for (var i = 0; i < 4; i++) flow.AddChild(new Control { CustomMinimumSize = new Vector2(30, 10), HorizontalSizeFlags = SizeFlags.ShrinkBegin, VerticalSizeFlags = SizeFlags.ShrinkBegin });
            var context = new UIContext(); context.Add(flow); context.Layout();

            Assert.That(flow.LineCount, Is.EqualTo(2));
            Assert.That(flow.LineMaxChildCount, Is.EqualTo(3));
            // Godot's real alignment-offset formula blends the last (non-full) line's leftover with the
            // PRIOR line's own post-expansion leftover (2px here), not a flat half-of-remaining split.
            Assert.That(flow.Children[3].Bounds, Is.EqualTo(new Rectangle(34, 14, 30, 10)));
            flow.ReverseFill = true; flow.QueueLayout(); context.Layout();
            // Godot's reverse_fill mirrors the CROSS axis (Y) for horizontal flow, not the main axis (X):
            // it flips which row stacks first, leaving each child's horizontal position untouched.
            Assert.That(flow.Children[0].Bounds.X, Is.EqualTo(0), "ReverseFill must not touch the main axis for horizontal flow.");
            Assert.That(flow.Children[0].Bounds.Y, Is.EqualTo(30));
            Assert.That(flow.Children[3].Bounds.Y, Is.EqualTo(16), "The second line now renders above the first, since ReverseFill flips which row is first.");
        }

        [Test]
        public void FlowContainer_LineMaxChildCountMatchesGodotsFirstLineColumnCountContract()
        {
            var flow = new HFlowContainer { Size = new Vector2(100, 40), Separation = 0 };
            foreach (var width in new[] { 70, 40, 30, 30 })
                flow.AddChild(new Control { CustomMinimumSize = new Vector2(width, 10) });
            var context = new UIContext(); context.Add(flow); context.Layout();

            Assert.That(flow.LineCount, Is.EqualTo(2));
            Assert.That(flow.LineMaxChildCount, Is.EqualTo(1),
                "Godot caches the first wrapped line's child count for editor column calculations, even when a later line contains more children.");
        }

            [Test]
            public void FlowContainer_WrapsAndSizesChildrenUsingBoundDesiredSizeLikeGodot()
            {
                var flow = new HFlowContainer { Size = new Vector2(100, 40), Separation = 0 };
                flow.AddChild(new DesiredSizeControl(new Vector2(70, 10)) { CustomMinimumSize = new Vector2(30, 10) });
                flow.AddChild(new Control { CustomMinimumSize = new Vector2(40, 10) });
                var context = new UIContext(); context.Add(flow); context.Layout();

                Assert.That(flow.LineCount, Is.EqualTo(2), "Godot wraps from get_bound_desired_size, so 70 + 40 exceeds the 100px line even though the first child's minimum is only 30.");
                Assert.That(flow.Children[0].Bounds, Is.EqualTo(new Rectangle(0, 0, 70, 10)));
                Assert.That(flow.Children[1].Bounds, Is.EqualTo(new Rectangle(0, 10, 40, 10)));
                Assert.That(flow.GetMinimumSize(), Is.EqualTo(new Vector2(40, 20)), "The public minimum-size query keeps the largest child minimum on the main axis while caching the desired-size wrapped cross extent.");
            }

            [Test]
            public void FlowContainer_RedistributesStretchAfterMaximumSizeCapsAChildLikeGodot()
            {
                var flow = new HFlowContainer { Size = new Vector2(100, 20), Separation = 0 };
                var capped = new Control
                {
                    CustomMinimumSize = new Vector2(20, 10),
                    CustomMaximumSize = new Vector2(30, -1),
                    HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand
                };
                var uncapped = new Control
                {
                    CustomMinimumSize = new Vector2(20, 10),
                    HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand
                };
                flow.AddChild(capped); flow.AddChild(uncapped);
                var context = new UIContext(); context.Add(flow); context.Layout();

                Assert.That(capped.Bounds.Width, Is.EqualTo(30));
                Assert.That(uncapped.Bounds.Width, Is.EqualTo(70), "Godot removes the capped child from the stretch-ratio pool and gives the remaining space to uncapped expand children.");
            }

        [Test]
        public void FlowContainer_MirrorsIndependentAxesForRtlAndReverseFillLikeGodot()
        {
            var flow = new HFlowContainer { Size = new Vector2(100, 40), Separation = 4 };
            for (var i = 0; i < 3; i++) flow.AddChild(new Control { CustomMinimumSize = new Vector2(30, 10), HorizontalSizeFlags = SizeFlags.ShrinkBegin, VerticalSizeFlags = SizeFlags.ShrinkBegin });
            var context = new UIContext(); context.Add(flow); context.Layout();
            Assert.That(flow.Children[0].Bounds.X, Is.EqualTo(0));

            flow.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.That(flow.Children[0].Bounds.X, Is.EqualTo(70), "Godot mirrors the main axis under RTL for horizontal flow.");

            flow.ReverseFill = true; flow.QueueLayout();
            context.Layout();
            // Godot's _resort mirrors reverse_fill's Y flip and rtl's X flip independently for horizontal
            // flow (they touch different axes), so they do not cancel each other out.
            Assert.That(flow.Children[0].Bounds.X, Is.EqualTo(70), "RTL's main-axis mirror is unaffected by ReverseFill, which only touches the cross axis.");
            Assert.That(flow.Children[0].Bounds.Y, Is.EqualTo(30), "ReverseFill still mirrors the cross axis under RTL.");

            var vflow = new VFlowContainer { Size = new Vector2(40, 100), Separation = 0 };
            var wide = new Control { CustomMinimumSize = new Vector2(30, 20) };
            var narrow = new Control { CustomMinimumSize = new Vector2(10, 20), HorizontalSizeFlags = SizeFlags.ShrinkEnd };
            vflow.AddChild(wide); vflow.AddChild(narrow);
            var vcontext = new UIContext(); vcontext.Add(vflow); vcontext.Layout();
            Assert.That(narrow.Bounds.X, Is.EqualTo(20));

            vflow.LayoutDirection = LayoutDirection.RightToLeft;
            vcontext.Layout();
            // Godot mirrors the cross axis (X) for vertical flow around the WHOLE container's own size
            // (get_rect().size.x = 40), not just the occupied line content width (30) - so the mirrored
            // position also absorbs the gap between the container and its content, not just the content itself.
            Assert.That(narrow.Bounds.X, Is.EqualTo(10), "Godot mirrors the cross axis for vertical flow under RTL, independent of ReverseFill (which targets the main axis in this port).");
        }

        [Test]
        public void FlowContainer_ReportsMinimumSizeAsMaxChildOnMainAxisAndWrappedExtentOnCrossAxisLikeGodot()
        {
            // Godot's FlowContainer::get_minimum_size only needs the single largest child on the main
            // axis (a tighter fit just wraps into more lines), but the FULL wrapped extent on the cross
            // axis (every line must actually be visible without clipping).
            var flow = new HFlowContainer { Size = new Vector2(100, 40), Separation = 4 };
            flow.AddChild(new Control { CustomMinimumSize = new Vector2(40, 10) });
            flow.AddChild(new Control { CustomMinimumSize = new Vector2(40, 10) });
            flow.AddChild(new Control { CustomMinimumSize = new Vector2(40, 10) });
            var context = new UIContext(); context.Add(flow); context.Layout();

            Assert.That(flow.LineCount, Is.EqualTo(2));
            Assert.That(flow.GetMinimumSize(), Is.EqualTo(new Vector2(40, 24)));
        }

        [Test]
        public void FlowContainer_CenterAlignmentDoesNotDoubleCountLeftoverWithAnExpandChildLikeGodot()
        {
            // Since this port has no maximum-size cap layer, an Expand child with a nonzero ratio always
            // consumes the ENTIRE line leftover; Center/End alignment must then add no additional offset
            // on top of that, or the expand child would be pushed past the container's own edge.
            var flow = new HFlowContainer { Size = new Vector2(100, 20), Separation = 0, Alignment = FlowAlignment.Center };
            var fixedChild = new Control { CustomMinimumSize = new Vector2(20, 10) };
            var expandChild = new Control { CustomMinimumSize = new Vector2(20, 10), HorizontalSizeFlags = SizeFlags.Expand | SizeFlags.Fill };
            flow.AddChild(fixedChild); flow.AddChild(expandChild);
            var context = new UIContext(); context.Add(flow); context.Layout();

            Assert.That(fixedChild.Bounds.X, Is.EqualTo(0));
            Assert.That(expandChild.Bounds.X + expandChild.Bounds.Width, Is.EqualTo(100), "The expand child must land exactly at the container's edge, not overflow past it.");
        }

        [Test]
        public void FlowContainer_AlignmentAndReverseFillSettersInvalidateLayoutLikeGodot()
        {
            var flow = new HFlowContainer { Size = new Vector2(100, 40), Separation = 4 };
            flow.AddChild(new Control { CustomMinimumSize = new Vector2(30, 10) });
            flow.AddChild(new Control { CustomMinimumSize = new Vector2(30, 10) });
            var context = new UIContext(); context.Add(flow); context.Layout();
            var beforeX = flow.Children[1].Bounds.X;

            // No manual QueueLayout() call here - the setter itself must invalidate, matching Godot's
            // set_alignment/set_last_wrap_alignment/set_reverse_fill each calling _resort() directly.
            flow.Alignment = FlowAlignment.End;
            context.Layout();
            Assert.That(flow.Children[1].Bounds.X, Is.Not.EqualTo(beforeX), "Changing Alignment after the first layout must re-arrange children on the next pass without an explicit QueueLayout call.");
        }

        [Test]
        public void FlowContainer_LastWrapAlignmentHasNoEffectWhenTheFinalLineIsFullLikeGodot()
        {
            // Unlike the earlier not-full-last-line case (which DOES apply LastWrapAlignment), a last line
            // that is packed full - appending one more copy of its own last child's minimum size would
            // overflow - is "is_filled" in Godot's real formula, so LastWrapAlignment must be ignored.
            var flow = new HFlowContainer { Size = new Vector2(100, 20), Separation = 0, LastWrapAlignment = FlowLastWrapAlignment.Center };
            flow.AddChild(new Control { CustomMinimumSize = new Vector2(100, 10) });
            flow.AddChild(new Control { CustomMinimumSize = new Vector2(90, 10) });
            var context = new UIContext(); context.Add(flow); context.Layout();

            Assert.That(flow.LineCount, Is.EqualTo(2));
            Assert.That(flow.Children[1].Bounds.X, Is.EqualTo(0), "A packed-full last line ignores LastWrapAlignment, matching Godot's is_filled gate.");
        }

        [Test]
        public void FlowContainer_OrientationCanChangeAtRuntimeUnlessFixedByHOrVFlowContainerLikeGodot()
        {
            var flow = new FlowContainer(Orientation.Horizontal);
            flow.Orientation = Orientation.Vertical;
            Assert.That(flow.Orientation, Is.EqualTo(Orientation.Vertical), "The base FlowContainer allows runtime reorientation, matching Godot's set_vertical when is_fixed is false.");

            var hflow = new HFlowContainer();
            hflow.Orientation = Orientation.Vertical;
            Assert.That(hflow.Orientation, Is.EqualTo(Orientation.Horizontal), "HFlowContainer fixes its orientation in the constructor, matching Godot's is_fixed guard rejecting set_vertical.");
        }

        [Test]
        public void FlowContainer_AZeroStretchRatioChildGetsNoStretchBonusLikeGodotSetStretchRatio()
        {
            // Godot's Control::set_stretch_ratio performs no clamping, so a true zero ratio is valid:
            // FlowContainer::_resort computes that child's stretch bonus as
            // line_remaining_stretch * 0 / line_stretch_ratio_total = 0, added on top of its own
            // minimum - it must not receive even a sliver of the line's leftover space.
            var flow = new HFlowContainer { Size = new Vector2(100, 20), Separation = 0 };
            var left = new Control { CustomMinimumSize = new Vector2(10, 10), HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand, SizeFlagsStretchRatio = 0 };
            var right = new Control { CustomMinimumSize = new Vector2(10, 10), HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand, SizeFlagsStretchRatio = 1 };
            flow.AddChild(left); flow.AddChild(right);
            var context = new UIContext(); context.Add(flow); context.Layout();

            Assert.That(left.Size.X, Is.EqualTo(10).Within(.001), "A zero ratio must not receive even a sliver of the line's stretch bonus.");
            Assert.That(right.Size.X, Is.EqualTo(90).Within(.001));
        }

        [Test]
        public void AspectRatioContainer_ClampsToChildMinimumSizeAndHonorsAlignment()
        {
            var aspect = new AspectRatioContainer { Size = new Vector2(100, 100), Ratio = 2, StretchMode = AspectRatioMode.Fit, AlignmentVertical = AspectRatioAlignment.End };
            var child = new Control { CustomMinimumSize = new Vector2(20, 10) };
            aspect.AddChild(child);
            var context = new UIContext(); context.Add(aspect); context.Layout();

            Assert.That(aspect.GetMinimumSize(), Is.EqualTo(new Vector2(20, 10)));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 50, 100, 50)));
            aspect.StretchMode = AspectRatioMode.HeightControlsWidth;
            aspect.AlignmentHorizontal = AspectRatioAlignment.End;
            context.Layout();
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(-100, 0, 200, 100)));
        }

        [Test]
        public void AspectRatioContainer_MirrorsHorizontalAlignmentUnderRtlLikeGodot()
        {
            var aspect = new AspectRatioContainer { Size = new Vector2(100, 50), Ratio = 1, AlignmentHorizontal = AspectRatioAlignment.Begin };
            var child = new Control();
            aspect.AddChild(child);
            var context = new UIContext(); context.Add(aspect); context.Layout();
            Assert.That(child.Bounds.X, Is.EqualTo(0));

            aspect.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.That(child.Bounds.X, Is.EqualTo(50), "Godot mirrors the fitted rect horizontally under RTL rather than swapping the alignment mode.");

            aspect.AlignmentHorizontal = AspectRatioAlignment.End;
            context.Layout();
            Assert.That(child.Bounds.X, Is.EqualTo(0));

            aspect.AlignmentHorizontal = AspectRatioAlignment.Center;
            context.Layout();
            Assert.That(child.Bounds.X, Is.EqualTo(25), "Center alignment is symmetric and unaffected by RTL.");
        }

        [Test]
        public void AspectRatioContainer_HonorsChildSizeFlagsViaFitChildInRectLikeGodot()
        {
            // Godot's AspectRatioContainer::_notification hands its aspect-fitted rect to
            // Container::fit_child_in_rect, which applies a SECOND layer on top of the aspect
            // computation: a non-Fill child is resized back down to its own minimum and aligned within
            // that rect via its own size flags, rather than being stretched to the aspect-computed size.
            var aspect = new AspectRatioContainer { Size = new Vector2(100, 100), Ratio = 1, StretchMode = AspectRatioMode.Fit };
            var child = new Control { CustomMinimumSize = new Vector2(20, 10), HorizontalSizeFlags = SizeFlags.ShrinkCenter };
            aspect.AddChild(child);
            var context = new UIContext(); context.Add(aspect); context.Layout();

            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(40, 0, 20, 100)), "Non-Fill horizontal shrinks to the child's own minimum and centers within the aspect box; Fill vertical still stretches to fill it.");
        }

        [Test]
        public void BoxContainer_AlignsAndReverseSortsFixedChildren()
        {
            var box = new HBoxContainer { Size = new Vector2(100, 20), Separation = 4, Alignment = BoxAlignment.Center };
            var first = new Control { CustomMinimumSize = new Vector2(20, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(30, 10) };
            box.AddChild(first); box.AddChild(second);
            var context = new UIContext(); context.Add(box); context.Layout();

            Assert.That(first.Bounds.X, Is.EqualTo(23));
            Assert.That(second.Bounds.X, Is.EqualTo(47));
            box.ReverseSort = true; context.Layout();
            Assert.That(second.Bounds.X, Is.EqualTo(23));
            Assert.That(first.Bounds.X, Is.EqualTo(57));
        }

        [Test]
        public void BoxContainer_CustomMinimumSizeIsAFloorRatherThanAddedToChildren()
        {
            var box = new HBoxContainer { CustomMinimumSize = new Vector2(100, 30), Separation = 4 };
            box.AddChild(new Control { CustomMinimumSize = new Vector2(20, 10) });
            box.AddChild(new Control { CustomMinimumSize = new Vector2(30, 40) });

            Assert.That(box.GetMinimumSize(), Is.EqualTo(new Vector2(100, 40)));
        }

        [Test]
        public void BoxContainer_HonorsChildCrossAxisSizeFlagsLikeGodotFitChildInRect()
        {
            // Regression test: BoxContainer previously stretched every child to the full cross-axis
            // length regardless of its perpendicular size flag, unlike Godot's fit_child_in_rect which
            // honors Shrink flags on that axis too (e.g. vertically centering a child in a toolbar row).
            var box = new HBoxContainer { Size = new Vector2(100, 40), Separation = 0 };
            var fillChild = new Control { CustomMinimumSize = new Vector2(20, 10) };
            var centered = new Control { CustomMinimumSize = new Vector2(20, 10), VerticalSizeFlags = SizeFlags.ShrinkCenter };
            var end = new Control { CustomMinimumSize = new Vector2(20, 10), VerticalSizeFlags = SizeFlags.ShrinkEnd };
            box.AddChild(fillChild); box.AddChild(centered); box.AddChild(end);
            var context = new UIContext(); context.Add(box); context.Layout();

            Assert.That(fillChild.Size.Y, Is.EqualTo(40), "Fill (default) stretches to the full cross-axis length.");
            Assert.That(centered.Size.Y, Is.EqualTo(10), "A non-Fill cross-axis child is sized to its own minimum instead of stretched.");
            Assert.That(centered.Position.Y, Is.EqualTo(15), "ShrinkCenter centers within the cross-axis span: (40-10)/2=15.");
            Assert.That(end.Size.Y, Is.EqualTo(10));
            Assert.That(end.Position.Y, Is.EqualTo(30), "ShrinkEnd aligns to the trailing edge: 40-10=30.");
        }

        [Test]
        public void MarginContainer_HonorsChildSizeFlagsLikeGodotFitChildInRect()
        {
            // Regression test: MarginContainer previously stretched every child to the full margin
            // rect on both axes, ignoring size flags entirely, unlike Godot which calls
            // fit_child_in_rect per child (margin_container.cpp), sizing a non-Fill child to its own
            // minimum and aligning it within the rect per its Shrink flag.
            var margin = new MarginContainer { ThemeOverrides = new Thickness(0), Size = new Vector2(100, 60) };
            var fillChild = new Control { CustomMinimumSize = new Vector2(20, 10) };
            var shrunk = new Control { CustomMinimumSize = new Vector2(20, 10), HorizontalSizeFlags = SizeFlags.ShrinkEnd, VerticalSizeFlags = SizeFlags.ShrinkCenter };
            margin.AddChild(fillChild); margin.AddChild(shrunk);
            var context = new UIContext(); context.Add(margin); context.Layout();

            Assert.That(fillChild.Size, Is.EqualTo(new Vector2(100, 60)), "A default Fill child still fills the margin rect.");
            Assert.That(shrunk.Size, Is.EqualTo(new Vector2(20, 10)), "A non-Fill child is sized to its own minimum instead of stretched.");
            Assert.That(shrunk.Position, Is.EqualTo(new Vector2(80, 25)), "ShrinkEnd (X: 100-20=80) and ShrinkCenter (Y: (60-10)/2=25) align within the margin rect.");
        }

        [Test]
        public void BoxContainer_AddSpacerInsertsAnExpandFillGapLikeGodot()
        {
            var box = new HBoxContainer { Size = new Vector2(100, 20), Separation = 0 };
            var first = new Control { CustomMinimumSize = new Vector2(20, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(20, 10) };
            box.AddChild(first); box.AddChild(second);

            var endSpacer = box.AddSpacer();
            Assert.That(box.Children, Is.EqualTo(new Control[] { first, second, endSpacer }), "Without begin, add_spacer appends the spacer.");
            Assert.That(endSpacer.MouseFilter, Is.EqualTo(MouseFilter.Pass), "Godot's add_spacer lets pointer events pass through the spacer.");
            Assert.That(endSpacer.HorizontalSizeFlags, Is.EqualTo(SizeFlags.Expand | SizeFlags.Fill), "The spacer expands along the box's own axis (horizontal here).");

            var context = new UIContext(); context.Add(box); context.Layout();
            Assert.That(first.Bounds, Is.EqualTo(new Rectangle(0, 0, 20, 20)));
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(20, 0, 20, 20)));
            Assert.That(endSpacer.Bounds, Is.EqualTo(new Rectangle(40, 0, 60, 20)), "The spacer consumes all leftover space as the only expanding child.");

            var beginSpacer = box.AddSpacer(begin: true);
            Assert.That(box.Children[0], Is.SameAs(beginSpacer), "begin: true moves the new spacer to the front of the child list.");
        }

        [Test]
        public void Button_ExposesGodotFlatAndTextAlignmentGeometry()
        {
            var button = new Button { Size = new Vector2(100, 30), TextAlignment = HorizontalAlignment.Right, Flat = true };

            Assert.That(button.Flat, Is.True);
            Assert.That(button.GetTextPosition(new Vector2(20, 10)), Is.EqualTo(new Vector2(72, 10)));
            button.TextAlignment = HorizontalAlignment.Left;
            Assert.That(button.GetTextPosition(new Vector2(20, 10)), Is.EqualTo(new Vector2(8, 10)));
        }

        [Test]
        public void Button_MapsGodotIconAlignmentExpansionAndRtl()
        {
            var button = new Button { Size = new Vector2(100, 30), IconAlignment = HorizontalAlignment.Right };
            Assert.That(button.GetIconRectangle(new Vector2(20, 10)), Is.EqualTo(new Rectangle(72, 10, 20, 10)));
            Assert.That(button.GetTextPosition(new Vector2(60, 10), new Vector2(20, 10)).X, Is.EqualTo(8), "Right-aligned icons must reserve separation from button text.");
            button.ExpandIcon = true;
            Assert.That(button.GetIconRectangle(new Vector2(20, 10)), Is.EqualTo(new Rectangle(48, 4, 44, 22)));
            button.ExpandIcon = false; button.IconAlignment = HorizontalAlignment.Left; button.LayoutDirection = LayoutDirection.RightToLeft;
            Assert.That(button.GetIconRectangle(new Vector2(20, 10)), Is.EqualTo(new Rectangle(72, 10, 20, 10)));
            Assert.That(button.GetTextPosition(new Vector2(60, 10), new Vector2(20, 10)).X, Is.EqualTo(8), "RTL icon mirroring must reserve separation on the mirrored side.");
        }

        [Test]
        public void Label_MapsGodotCasingVisibleCharactersAndLineWindows()
        {
            var label = new Label { Text = "ab\ncd", Uppercase = true, VisibleCharacters = 4, LinesSkipped = 1, MaxLinesVisible = 1 };

            Assert.That(label.GetDisplayLines(), Is.EqualTo(new[] { "C" }));
            label.VisibleCharacters = -1;
            label.VisibleRatio = .5f;
            label.LinesSkipped = 0;
            Assert.That(label.GetDisplayLines(), Is.EqualTo(new[] { "AB" }));
        }

        [Test]
        public void Label_ResyncsVisibleCharactersWhenTextChangesLikeGodotSetText()
        {
            // Godot's Label::set_text resyncs visible_chars to keep an absolute visible-character count
            // proportionally consistent with the new text's length, whenever a ratio below 1 is active.
            var label = new Label { Text = new string('a', 100) };
            label.SetVisibleCharacters(4);
            Assert.That(label.GetVisibleRatio(), Is.EqualTo(0.04f).Within(.001f));

            label.Text = new string('b', 10);
            Assert.That(label.GetVisibleCharacters(), Is.EqualTo(0), "10 * 0.04 = 0.4, truncated to 0 - nothing visible after the resync.");
        }

        [Test]
        public void Label_SetVisibleCharactersDoesNotClampTheDerivedRatioLikeGodot()
        {
            // Godot's set_visible_characters performs no clamping on the derived ratio at all.
            var label = new Label { Text = "abc" };
            label.SetVisibleCharacters(10);
            Assert.That(label.GetVisibleRatio(), Is.EqualTo(10f / 3f).Within(.001f));
        }

        [Test]
        public void Label_VisibleCharactersBehaviorGatesWhetherLineLayoutIsTruncated()
        {
            // Godot only substrs the text for VC_CHARS_BEFORE_SHAPING; every other behavior shapes/wraps
            // the FULL text (only per-glyph draw-time hiding differs, which this port doesn't model), so
            // line count/wrapping must still reflect the full text for those behaviors.
            var label = new Label { Text = "one two three four", VisibleCharacters = 3 };
            Assert.That(label.GetDisplayLines(), Is.EqualTo(new[] { "one" }), "Default CharactersBeforeShaping truncates the layout text.");

            label.VisibleCharactersBehavior = LabelVisibleCharactersBehavior.CharactersAfterShaping;
            Assert.That(label.GetDisplayLines(), Is.EqualTo(new[] { "one two three four" }), "Other behaviors lay out the full text.");
        }

        [Test]
        public void Label_GetMinimumSizeCollapsesTheClippedOrTrimmedAxisLikeGodot()
        {
            var font = CreateTestFont();
            var plain = new Label { Font = font, Text = "A long line of label text" };
            var plainSize = plain.GetMinimumSize();
            Assert.That(plainSize.X, Is.GreaterThan(10));

            // Godot: clipping/trimming without autowrap collapses the WIDTH to a nominal minimum, since
            // that's what lets a label shrink below its natural text size in a layout container.
            var clipped = new Label { Font = font, Text = "A long line of label text", ClipText = true };
            Assert.That(clipped.GetMinimumSize().X, Is.EqualTo(1 + clipped.Padding.Horizontal));

            // With autowrap, clipping/trimming instead collapses the HEIGHT.
            var wrappedClipped = new Label { Font = font, Text = "A long line of label text", AutowrapMode = LabelAutowrapMode.WordSmart, ClipText = true };
            Assert.That(wrappedClipped.GetMinimumSize().X, Is.EqualTo(wrappedClipped.Padding.Horizontal), "Autowrap always collapses width, clipped or not.");
            Assert.That(wrappedClipped.GetMinimumSize().Y, Is.EqualTo(1 + wrappedClipped.Padding.Vertical));
        }

        [Test]
        public void WrappedBacklogRowsDoNotOverlapAndReflowInsideAScrollContainer()
        {
            using var context = new UIContext { ViewportSize = new Vector2(180, 120) };
            var scroll = new ScrollContainer
            {
                Size = context.ViewportSize,
                HorizontalScrollMode = ScrollBarVisibility.Disabled
            };
            var items = new BoxContainer(Orientation.Vertical)
            {
                Separation = 10,
                HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand
            };
            scroll.AddChild(items);
            var labels = new List<Label>();
            for (var i = 0; i < 5; i++)
            {
                var row = new BoxContainer(Orientation.Vertical) { Margins = new Thickness(8, 6, 8, 6) };
                var label = new Label
                {
                    UIFont = new SpriteFontAdapter(CreateTestFont()),
                    Text = "Speaker\nA long backlog paragraph with words that wrap over several lines.",
                    AutowrapMode = LabelAutowrapMode.WordSmart,
                    CustomMinimumSize = new Vector2(0, 40)
                };
                row.AddChild(label);
                items.AddChild(row);
                labels.Add(label);
            }
            context.Add(scroll);
            void Settle()
            {
                for (var frame = 0; frame < 8; frame++)
                    context.Update(Time, Mouse(0, 0), new KeyboardState());
                for (var i = 0; i < labels.Count; i++)
                {
                    var label = labels[i];
                    var last = label.GetCharacterBounds(label.Text.Length - 1);
                    Assert.That(last, Is.Not.EqualTo(Rectangle.Empty));
                    Assert.That(label.Size.Y, Is.GreaterThanOrEqualTo(last.Bottom));
                    if (i + 1 < labels.Count)
                        Assert.That(labels[i + 1].GlobalPosition.Y,
                            Is.GreaterThanOrEqualTo(label.GlobalPosition.Y + last.Bottom + 10));
                    Assert.That(label.Size.X, Is.LessThanOrEqualTo(scroll.Size.X - 16));
                }
            }
            Settle();
            var narrowHeight = items.GetMinimumSize().Y;
            scroll.Size = new Vector2(420, 120);
            Settle();
            Assert.That(items.GetMinimumSize().Y, Is.LessThan(narrowHeight));
            labels[0].Text += " An additional paragraph that must make this row taller after editing.";
            Settle();
            scroll.Size = new Vector2(180, 120);
            Settle();
        }

        [Test]
        public void Label_EmptyParagraphSeparatorDisablesSplittingLikeGodot()
        {
            // Godot's set_paragraph_separator assigns the passed string directly with no empty-string
            // special-casing - an empty separator is a legitimate way to treat the whole text as one
            // paragraph.
            var label = new Label { Text = "line one\nline two" };
            label.SetParagraphSeparator("");
            Assert.That(label.GetParagraphSeparator(), Is.Empty);
            Assert.That(label.GetDisplayLines(), Is.EqualTo(new[] { "line one\nline two" }));
        }

        [Test]
        public void Label_ForcesEllipsisOnLastVisibleLineWhenAutowrapHidesLinesLikeGodot()
        {
            // Godot forces an ellipsis on the last visible line when autowrap produced more lines than
            // MaxLinesVisible allows, even when TextOverrunBehavior is NoTrimming (the default).
            var font = CreateTestFont();
            var label = new Label { Font = font, Text = "one two three four five six", AutowrapMode = LabelAutowrapMode.WordSmart, MaxLinesVisible = 2, Size = new Vector2(60, 100), EllipsisCharacter = "..." };
            var lines = label.GetDisplayLines();
            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[1], Does.EndWith(label.EllipsisCharacter));
        }

        [Test]
        public void LineEdit_MapsGodotClearButtonAndFocusExitSubmission()
        {
            var first = new LineEdit { Text = "Search", Size = new Vector2(100, 24), ClearButtonEnabled = true, SubmitOnFocusExit = true };
            var second = new LineEdit { Position = new Vector2(110, 0), Size = new Vector2(100, 24) };
            var submissions = 0; first.TextSubmitted += (_, _) => submissions++;
            var context = new UIContext(); context.Add(first); context.Add(second);
            var clear = first.GetClearButtonRectangle();

            context.Update(Time, Mouse(clear.Center.X, clear.Center.Y), new KeyboardState());
            context.Update(Time, Mouse(clear.Center.X, clear.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(clear.Center.X, clear.Center.Y), new KeyboardState());
            first.GrabFocus(); second.GrabFocus();

            Assert.That(clear, Is.EqualTo(new Rectangle(78, 4, 16, 16)));
            Assert.That(first.Text, Is.Empty);
            Assert.That(submissions, Is.EqualTo(1));
        }

        [Test]
        public void LineEdit_MapsGodotRetainedContextMenuClipboardAndFocusApis()
        {
            var edit = new LineEdit { Text = "value", Size = new Vector2(160, 24) };
            var copied = string.Empty;
            edit.CopyRequested += (_, text) => copied = text;
            edit.ClearUndoHistory();
            var clipboard = new TestClipboard();
            var context = new UIContext { Clipboard = clipboard }; context.Add(edit);

            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, right: ButtonState.Pressed), new KeyboardState());

            var menu = edit.GetMenu();
            Assert.That(edit.IsMenuVisible(), Is.True);
            Assert.That(menu.Items, Has.Count.EqualTo(14));
            Assert.That(menu.Items[0].Text, Is.EqualTo("Cut"));
            Assert.That(menu.Items[4].Text, Is.EqualTo("Select All"));
            Assert.That(menu.Items[7].Disabled, Is.True);
            Assert.That(menu.Items[8].Disabled, Is.True);

            edit.MenuOption(LineEditMenuOption.Copy);
            Assert.That(copied, Is.Empty, "Godot LineEdit only copies active selections.");
            edit.MenuOption(LineEditMenuOption.SelectAll);
            edit.MenuOption(LineEditMenuOption.Copy);
            Assert.That(copied, Is.EqualTo("value"));
            Assert.That(clipboard.Text, Is.EqualTo("value"));

            edit.MenuOption(LineEditMenuOption.Cut);
            Assert.That(copied, Is.EqualTo("value"));
            Assert.That(clipboard.Text, Is.EqualTo("value"));
            Assert.That(edit.Text, Is.Empty);

            clipboard.Text = "pa\nst\te";
            edit.MenuOption(LineEditMenuOption.Paste);
            Assert.That(edit.Text, Is.EqualTo("paste"));

            edit.SetContextMenuEnabled(false);
            menu.Hide();
            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, right: ButtonState.Pressed), new KeyboardState());
            Assert.That(edit.IsMenuVisible(), Is.False);

            edit.SetSelectAllOnFocus(true);
            edit.Text = "focus";
            edit.Deselect();
            edit.GrabFocus();
            Assert.That(edit.SelectedText, Is.EqualTo("focus"));
            Assert.That(edit.IsSelectAllOnFocus(), Is.True);
        }

        [Test]
        public void LineEdit_MapsGodotUndoRedoHistoryAndMenuState()
        {
            var edit = new LineEdit { Text = "ab", Size = new Vector2(160, 24) };
            edit.ClearUndoHistory();
            edit.Select(2, 2);
            edit.InsertText("c");
            edit.DeleteText(1, 2);

            Assert.That(edit.Text, Is.EqualTo("ac"));
            Assert.That(edit.HasUndo, Is.True);
            Assert.That(edit.HasRedo, Is.False);

            edit.Undo();

            Assert.That(edit.Text, Is.EqualTo("abc"));
            Assert.That(edit.HasRedo, Is.True);

            edit.Redo();

            Assert.That(edit.Text, Is.EqualTo("ac"));

            var context = new UIContext(); context.Add(edit);
            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, right: ButtonState.Pressed), new KeyboardState());
            var menu = edit.GetMenu();

            Assert.That(menu.Items[7].Text, Is.EqualTo("Undo"));
            Assert.That(menu.Items[7].Disabled, Is.False);
            Assert.That(menu.Items[8].Text, Is.EqualTo("Redo"));
            Assert.That(menu.Items[8].Disabled, Is.True);

            edit.MenuOption(LineEditMenuOption.Undo);
            Assert.That(edit.Text, Is.EqualTo("abc"));
            edit.MenuOption(LineEditMenuOption.Redo);
            Assert.That(edit.Text, Is.EqualTo("ac"));

            menu.Hide();
            edit.GrabFocus();
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.Z));
            Assert.That(edit.Text, Is.EqualTo("abc"));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.Y));
            Assert.That(edit.Text, Is.EqualTo("ac"));

            edit.Editable = false;
            edit.Undo();
            Assert.That(edit.Text, Is.EqualTo("ac"));
        }

        [Test]
        public void LineEdit_MapsGodotDirectionControlCharacterAndStructuredTextState()
        {
            var edit = new LineEdit { Text = "ab", Size = new Vector2(180, 24) };

            Assert.That(edit.GetTextDirection(), Is.EqualTo(TextDirection.Auto));
            Assert.That(edit.GetLanguage(), Is.Empty);
            Assert.That(edit.GetDrawControlChars(), Is.False);
            Assert.That(edit.GetStructuredTextBidiOverride(), Is.EqualTo(StructuredTextParser.Default));

            edit.SetTextDirection(TextDirection.RightToLeft);
            edit.SetLanguage("ar");
            edit.SetDrawControlChars(true);
            edit.SetStructuredTextBidiOverride(StructuredTextParser.Email);
            edit.SetStructuredTextBidiOverrideOptions(new object[] { "domain", 7 });

            Assert.That(edit.GetTextDirection(), Is.EqualTo(TextDirection.RightToLeft));
            Assert.That(edit.GetLanguage(), Is.EqualTo("ar"));
            Assert.That(edit.GetDrawControlChars(), Is.True);
            Assert.That(edit.GetStructuredTextBidiOverride(), Is.EqualTo(StructuredTextParser.Email));
            Assert.That(edit.GetStructuredTextBidiOverrideOptions(), Is.EqualTo(new object[] { "domain", 7 }));

            var context = new UIContext(); context.Add(edit);
            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, right: ButtonState.Pressed), new KeyboardState());

            var menu = edit.GetMenu();
            Assert.That(menu.Items[10].Text, Is.EqualTo("Text Writing Direction"));
            Assert.That(menu.Items[12].Text, Is.EqualTo("Display Control Characters"));
            Assert.That(menu.Items[12].Checked, Is.True);
            Assert.That(menu.Items[13].Text, Is.EqualTo("Insert Control Character"));
            Assert.That(menu.Items[13].Disabled, Is.False);

            var directionMenu = edit.GetTextDirectionMenu();
            Assert.That(directionMenu.Items, Has.Count.EqualTo(4));
            Assert.That(directionMenu.Items[3].Checked, Is.True);

            edit.MenuOption(LineEditMenuOption.DirectionLeftToRight);
            Assert.That(edit.GetTextDirection(), Is.EqualTo(TextDirection.LeftToRight));
            edit.MenuOption(LineEditMenuOption.DisplayControlCharacters);
            Assert.That(edit.GetDrawControlChars(), Is.False);

            edit.Select(1, 1);
            edit.MenuOption(LineEditMenuOption.InsertRightToLeftMark);
            edit.MenuOption(LineEditMenuOption.InsertZeroWidthJoiner);

            Assert.That(edit.Text, Is.EqualTo("a\u200F\u200Db"));
            Assert.That(edit.CaretColumn, Is.EqualTo(3));

            var controlMenu = edit.GetControlCharacterMenu();
            Assert.That(controlMenu.Items[0].Text, Does.Contain("LRM"));
            Assert.That(controlMenu.Items[8].Text, Does.Contain("ALM"));
            Assert.That(controlMenu.Items[17].Text, Does.Contain("SHY"));

            edit.Editable = false;
            menu.Hide();
            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, right: ButtonState.Pressed), new KeyboardState());

            Assert.That(edit.GetMenu().Items[13].Disabled, Is.True);
        }

        [Test]
        public void LineEdit_MapsGodotShortcutKeysAndPasswordClipboardPolicy()
        {
            var edit = new LineEdit { Text = "secret", SecretCharacter = "*", Size = new Vector2(160, 24), ClipboardTextProvider = _ => "X" };
            var copied = string.Empty;
            edit.CopyRequested += (_, text) => copied = text;
            var context = new UIContext(new TestClipboard()); context.Add(edit); edit.GrabFocus();
            edit.SelectAll();

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.C));
            Assert.That(copied, Is.Empty, "Secret LineEdit content should not be copied.");

            edit.SecretCharacter = string.Empty;
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.C));
            Assert.That(copied, Is.EqualTo("secret"));

            edit.SetShortcutKeysEnabled(false);
            edit.Deselect();
            edit.Select(0, 0);
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.V));
            Assert.That(edit.Text, Is.EqualTo("secret"));
            Assert.That(edit.IsShortcutKeysEnabled(), Is.False);
        }

        [Test]
        public void TreeItem_MapsGodotCheckAndRangeCellModes()
        {
            var tree = new Tree { Size = new Vector2(160, 50), Columns = 2 };
            var item = tree.CreateItem(); item.SetText(0, "Visible"); item.SetCellMode(0, TreeCellMode.Check); item.SetChecked(0, true); item.SetEditable(0, true); item.SetRangeConfig(1, 10, 20, 2, true); item.SetRange(1, 16); item.SetEditable(1, true); item.SetTooltipText(1, "Opacity");
            var edits = 0; tree.ItemEdited += (_, _, _) => edits++;
            var context = new UIContext(); context.Add(tree);
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(154, 18), new KeyboardState());
            context.Update(Time, Mouse(154, 18, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(154, 18), new KeyboardState());

            Assert.That(item.GetCellMode(0), Is.EqualTo(TreeCellMode.Check));
            Assert.That(item.IsChecked(0), Is.False);
            Assert.That(item.GetCellMode(1), Is.EqualTo(TreeCellMode.Range));
            item.GetRangeConfig(1, out var minimum, out var maximum, out var step);
            Assert.That(item.GetRange(1), Is.EqualTo(14));
            Assert.That((minimum, maximum, step), Is.EqualTo((10f, 20f, 2f)));
            Assert.That(item.IsRangeExponential(1), Is.True);
            Assert.That(tree.GetTooltip(new Point(120, 10)), Is.EqualTo("Opacity"));
            Assert.That(edits, Is.EqualTo(2));
        }

        [Test]
        public void Tree_MapsGodotRangeSteppersAndDiscreteRangePopup()
        {
            var tree = new Tree { Size = new Vector2(120, 56) };
            var numeric = tree.CreateItem(); numeric.SetRangeConfig(0, 0, 10, 2); numeric.SetRange(0, 4); numeric.SetEditable(0, true);
            var choice = tree.CreateItem(); choice.SetRangeConfig(0, 0, 4, 1); choice.SetText(0, "Low:0,Medium:2,High:4"); choice.SetRange(0, 0); choice.SetEditable(0, true);
            var edits = 0; tree.ItemEdited += (_, _, _) => edits++;
            var context = new UIContext(); context.Add(tree); context.Layout();
            var numericCell = tree.GetItemAreaRectangle(numeric, 0);
            var numericSpinner = new Point(numericCell.Right - 4, numericCell.Bottom - 4);

            context.Update(Time, Mouse(numericSpinner.X, numericSpinner.Y), new KeyboardState());
            context.Update(Time, Mouse(numericSpinner.X, numericSpinner.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(numericSpinner.X, numericSpinner.Y), new KeyboardState());
            Assert.That(numeric.GetRange(0), Is.EqualTo(2));
            var upSpinner = new Point(numericSpinner.X, numericCell.Top + 4);
            context.Update(Time, Mouse(upSpinner.X, upSpinner.Y, right: ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(upSpinner.X, upSpinner.Y), new KeyboardState());
            Assert.That(numeric.GetRange(0), Is.EqualTo(10));
            Assert.That(tree.GetItemAtPosition(numericSpinner), Is.SameAs(numeric));
            Assert.That(context.HitTest(numericSpinner), Is.SameAs(tree));
            context.Update(Time, Mouse(numericSpinner.X, numericSpinner.Y), new KeyboardState());
            context.Update(Time, Mouse(numericSpinner.X, numericSpinner.Y, scrollWheel: -120), new KeyboardState());
            Assert.That(numeric.GetRange(0), Is.EqualTo(8));

            var choiceCell = tree.GetItemAreaRectangle(choice, 0);
            context.Update(Time, Mouse(choiceCell.Center.X, choiceCell.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(choiceCell.Center.X, choiceCell.Center.Y), new KeyboardState());
            var popup = tree.GetRangePopup();
            Assert.That(popup.Visible, Is.True);
            Assert.That(popup.Items.Count, Is.EqualTo(3));
            Assert.That(popup.Items[1].Text, Is.EqualTo("Medium"));
            var option = new Point(popup.Bounds.Center.X, popup.Bounds.Y + 1 + (int)popup.ItemHeight + (int)popup.ItemHeight / 2);
            context.Update(Time, Mouse(option.X, option.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(option.X, option.Y), new KeyboardState());

            Assert.That(choice.GetRange(0), Is.EqualTo(2));
            Assert.That(choice.GetDisplayText(0), Is.EqualTo("Medium"));
            Assert.That(popup.Visible, Is.False);
            Assert.That(edits, Is.EqualTo(4));
        }

        [Test]
        public void Tree_MapsGodotHeldNumericRangeStepRepeat()
        {
            var tree = new Tree { Size = new Vector2(120, 32) };
            var item = tree.CreateItem(); item.SetRangeConfig(0, 0, 20, 2); item.SetRange(0, 10); item.SetEditable(0, true);
            var edits = 0; tree.ItemEdited += (_, edited, _) => { if (edited == item) edits++; };
            var context = new UIContext(); context.Add(tree); context.Layout();
            var cell = tree.GetItemAreaRectangle(item, 0); var lowerStepper = new Point(cell.Right - 4, cell.Bottom - 4);

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(lowerStepper.X, lowerStepper.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(item.GetRange(0), Is.EqualTo(8));
            context.Update(new GameTime(TimeSpan.FromMilliseconds(590), TimeSpan.FromMilliseconds(590)), Mouse(lowerStepper.X, lowerStepper.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(item.GetRange(0), Is.EqualTo(8));
            context.Update(new GameTime(TimeSpan.FromMilliseconds(610), TimeSpan.FromMilliseconds(20)), Mouse(lowerStepper.X, lowerStepper.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(item.GetRange(0), Is.EqualTo(6));
            context.Update(new GameTime(TimeSpan.FromMilliseconds(660), TimeSpan.FromMilliseconds(50)), Mouse(lowerStepper.X, lowerStepper.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(item.GetRange(0), Is.EqualTo(4));
            context.Update(new GameTime(TimeSpan.FromMilliseconds(660), TimeSpan.Zero), Mouse(lowerStepper.X, lowerStepper.Y), new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1340)), Mouse(lowerStepper.X, lowerStepper.Y), new KeyboardState());

            Assert.That(item.GetRange(0), Is.EqualTo(4));
            Assert.That(edits, Is.EqualTo(3));
        }

        [Test]
        public void Tree_MapsGodotVerticalRangeDragAdjustment()
        {
            var tree = new Tree { Size = new Vector2(120, 32) };
            var item = tree.CreateItem(); item.SetRangeConfig(0, 0, 100, 1); item.SetRange(0, 50); item.SetEditable(0, true);
            var edits = 0; tree.ItemEdited += (_, edited, column) => { if (edited == item && column == 0) edits++; };
            var context = new UIContext(); context.Add(tree); context.Layout();
            var cell = tree.GetItemAreaRectangle(item, 0);
            var start = new Point(cell.X + 20, cell.Y + cell.Height / 2);

            context.Update(Time, Mouse(start.X, start.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(start.X, start.Y + 8, ButtonState.Pressed), new KeyboardState());
            Assert.That(item.GetRange(0), Is.EqualTo(50), "Crossing the drag threshold does not adjust until a relative motion arrives.");
            context.Update(Time, Mouse(start.X, start.Y - 2, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(start.X, start.Y - 2), new KeyboardState());

            Assert.That(item.GetRange(0), Is.EqualTo(56));
            Assert.That(edits, Is.EqualTo(1));
        }

        [Test]
        public void Tree_MapsGodotInlineNumericRangeEditorCommitAndCancel()
        {
            var tree = new Tree { Size = new Vector2(120, 32) };
            var item = tree.CreateItem(); item.SetRangeConfig(0, 0, 100, 5, exponential: true); item.SetRange(0, 60); item.SetEditable(0, true);
            var edits = 0; tree.ItemEdited += (_, edited, column) => { if (edited == item && column == 0) edits++; };
            var context = new UIContext(); context.Add(tree); context.Layout();
            var cell = tree.GetItemAreaRectangle(item, 0); var body = new Point(cell.X + 20, cell.Center.Y);

            context.Update(Time, Mouse(body.X, body.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(body.X, body.Y), new KeyboardState());
            var popup = tree.GetRangeEditorPopup(); var editor = tree.GetRangeEditorLineEdit(); var slider = tree.GetRangeEditorSlider();
            Assert.That(popup.Visible, Is.True);
            Assert.That(editor.Text, Is.EqualTo("60"));
            Assert.That((slider.MinValue, slider.MaxValue, slider.Step, slider.ExpRatio), Is.EqualTo((0f, 100f, 5f, true)));
            slider.Value = 40;
            Assert.That(editor.Text, Is.EqualTo("40"));
            editor.Text = "73"; editor.KeyPressed(Keys.Enter);
            Assert.That(item.GetRange(0), Is.EqualTo(75));
            Assert.That(popup.Visible, Is.False);
            Assert.That(edits, Is.EqualTo(1));

            Assert.That(tree.EditSelected(), Is.True);
            Assert.That(popup.Visible, Is.True);
            editor.Text = "20"; editor.KeyPressed(Keys.Escape);
            Assert.That(item.GetRange(0), Is.EqualTo(75));
            Assert.That(popup.Visible, Is.False);
            Assert.That(edits, Is.EqualTo(1));
        }

        [Test]
        public void Tree_MapsGodotStringAndMultilineCellEditorLifecycle()
        {
            var tree = new Tree { Size = new Vector2(180, 64), Columns = 2 };
            var item = tree.CreateItem(); item.SetText(0, "Camera2D"); item.SetEditable(0, true); item.SetText(1, "first line"); item.SetEditable(1, true); item.SetEditMultiline(1, true);
            var edits = 0; tree.ItemEdited += (_, edited, _) => { if (edited == item) edits++; };
            var context = new UIContext(); context.Add(tree); context.Layout();

            var singleCell = tree.GetItemAreaRectangle(item, 0);
            context.Update(Time, Mouse(singleCell.Center.X, singleCell.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(singleCell.Center.X, singleCell.Center.Y), new KeyboardState());
            var popup = tree.GetStringEditorPopup(); var line = tree.GetStringEditorLineEdit(); var multiline = tree.GetStringEditorTextEdit();
            Assert.That(popup.Visible, Is.True);
            Assert.That(popup.Bounds, Is.EqualTo(new Rectangle(singleCell.X, singleCell.Y, Math.Max(80, singleCell.Width), Math.Max(singleCell.Height, (int)line.GetMinimumSize().Y))));
            Assert.That(line.Visible, Is.True);
            Assert.That(multiline.Visible, Is.False);
            Assert.That(line.Text, Is.EqualTo("Camera2D"));
            line.Text = "Lens"; line.KeyPressed(Keys.Enter);
            Assert.That(item.GetText(0), Is.EqualTo("Lens"));
            Assert.That(popup.Visible, Is.False);

            tree.Select(item, 1);
            Assert.That(tree.EditSelected(), Is.True);
            Assert.That(popup.Visible, Is.True);
            Assert.That(line.Visible, Is.False);
            Assert.That(multiline.Visible, Is.True);
            multiline.Text = "cancelled"; multiline.KeyPressed(Keys.Escape);
            Assert.That(item.GetText(1), Is.EqualTo("first line"));
            Assert.That(popup.Visible, Is.False);

            Assert.That(tree.EditSelected(), Is.True);
            multiline.Text = "programmatic close"; popup.Hide();
            Assert.That(item.GetText(1), Is.EqualTo("programmatic close"));

            Assert.That(tree.EditSelected(), Is.True);
            multiline.Text = "Ctrl+Enter commits";
            Assert.That(context.FocusedControl, Is.SameAs(multiline));
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            Assert.That(context.FocusedControl, Is.SameAs(multiline));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.Enter));
            Assert.That(item.GetText(1), Is.EqualTo("Ctrl+Enter commits"));
            Assert.That(popup.Visible, Is.False);
            Assert.That(edits, Is.EqualTo(3));
        }

        [Test]
        public void TreeItem_MapsGodotCellPresentationAndMultilineEditMetadata()
        {
            var tree = new Tree { Columns = 2 };
            var item = tree.CreateItem();

            Assert.That(item.GetDescription(0), Is.Empty);
            Assert.That(item.GetTextDirection(0), Is.EqualTo(TextDirection.Inherited));
            Assert.That(item.GetLanguage(0), Is.Empty);
            Assert.That(item.GetSuffix(0), Is.Empty);
            Assert.That(item.IsEditMultiline(0), Is.False);

            item.SetDescription(0, "Opacity percentage");
            item.SetTextDirection(0, TextDirection.RightToLeft);
            item.SetLanguage(0, "ar");
            item.SetSuffix(0, "%");
            item.SetRangeConfig(0, 0, 100, .25f);
            item.SetRange(0, 12.5f);
            item.SetEditMultiline(1, true);
            item.SetDescription(1, null);
            item.SetLanguage(1, null);
            item.SetSuffix(1, null);

            Assert.That(item.GetDescription(0), Is.EqualTo("Opacity percentage"));
            Assert.That(item.GetTextDirection(0), Is.EqualTo(TextDirection.RightToLeft));
            Assert.That(item.GetLanguage(0), Is.EqualTo("ar"));
            Assert.That(item.GetSuffix(0), Is.EqualTo("%"));
            Assert.That(item.GetDisplayText(0), Is.EqualTo("12.5 %"));
            Assert.That(item.IsEditMultiline(1), Is.True);
            Assert.That(item.GetDescription(1), Is.Empty);
            Assert.That(item.GetLanguage(1), Is.Empty);
            Assert.That(item.GetSuffix(1), Is.Empty);
        }

        [Test]
        public void TreeItem_MapsGodotDiscreteRangeTextDisplay()
        {
            var tree = new Tree();
            var item = tree.CreateItem();
            item.SetRangeConfig(0, 0, 3, 1);
            item.SetText(0, "Hidden:0,Locked,Visible:3");
            item.SetEditable(0, true);
            item.SetSuffix(0, "state");

            item.SetRange(0, 3);
            Assert.That(item.GetDisplayText(0), Is.EqualTo("Visible state"));
            item.SetRange(0, 1);
            Assert.That(item.GetDisplayText(0), Is.EqualTo("Locked state"));
            item.SetRange(0, 2);
            Assert.That(item.GetDisplayText(0), Is.EqualTo("(Other) state"));
            item.SetRange(0, 0);
            item.SetEditable(0, false);
            Assert.That(item.GetDisplayText(0), Is.Empty);
        }

        [Test]
        public void Control_PropagatesGodotRtlDirectionToHorizontalBoxLayout()
        {
            var box = new HBoxContainer { Size = new Vector2(100, 20), Separation = 4, LayoutDirection = LayoutDirection.RightToLeft };
            var first = new Control { CustomMinimumSize = new Vector2(20, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(30, 10) };
            box.AddChild(first); box.AddChild(second);
            var context = new UIContext(); context.Add(box); context.Layout();

            Assert.That(box.IsLayoutRtl(), Is.True);
            Assert.That(first.Bounds.X, Is.EqualTo(80));
            Assert.That(second.Bounds.X, Is.EqualTo(46));
            first.LayoutDirection = LayoutDirection.LeftToRight;
            Assert.That(first.IsLayoutRtl(), Is.False);
        }

        [Test]
        public void Control_QueueLayoutPropagatesThroughEveryAncestorLikeGodotUpdateMinimumSize()
        {
            // Regression test: QueueLayout previously only dirtied the immediate parent, matching
            // neither Godot's Control::update_minimum_size (which walks the whole ancestor chain) nor
            // Container::_child_minsize_changed (which requeues sorting further up when a deeply nested
            // child's minimum size changes). A grandchild's size change must reach the grandparent too.
            var grandparent = new MarginContainer { ThemeOverrides = new Thickness(0), Size = new Vector2(200, 100) };
            var parent = new HBoxContainer();
            var child = new Control { CustomMinimumSize = new Vector2(20, 10) };
            parent.AddChild(child);
            grandparent.AddChild(parent);
            var context = new UIContext(); context.Add(grandparent); context.Layout();

            var grandparentLaidOut = 0; grandparent.LayoutChanged += (_, _) => grandparentLaidOut++;
            var parentLaidOut = 0; parent.LayoutChanged += (_, _) => parentLaidOut++;

            child.CustomMinimumSize = new Vector2(80, 10);
            context.Layout();

            Assert.That(parentLaidOut, Is.EqualTo(1), "The immediate parent must re-arrange when a child's minimum size changes.");
            Assert.That(grandparentLaidOut, Is.EqualTo(1), "The whole ancestor chain must be requeued, not just one level, so the grandparent also re-arranges.");
        }

        [Test]
        public void Control_TogglingVisibleRequeuesParentLayoutLikeGodotVisibilityChanged()
        {
            // Regression test: Godot wires every child's visibility_changed signal to the parent
            // Container's queue_sort(); the C# Visible property previously had zero side effects, so
            // hiding a sibling never caused the remaining children to reflow into the freed space.
            var box = new HBoxContainer { Size = new Vector2(100, 20), Separation = 0 };
            var first = new Control { CustomMinimumSize = new Vector2(40, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(40, 10) };
            box.AddChild(first); box.AddChild(second);
            var context = new UIContext(); context.Add(box); context.Layout();
            Assert.That(second.Bounds.X, Is.EqualTo(40));

            first.Visible = false;
            context.Layout();
            Assert.That(second.Bounds.X, Is.EqualTo(0), "Hiding a sibling must re-run the box's layout so the remaining child fills the freed space.");

            first.Visible = true;
            context.Layout();
            Assert.That(second.Bounds.X, Is.EqualTo(40), "Showing it again must also requeue layout.");
        }

        [Test]
        public void Control_ResolvesGodotApplicationSystemAndRootLocaleDirections()
        {
            var context = new UIContext { ApplicationCulture = new CultureInfo("ar-SA"), SystemCulture = new CultureInfo("en-US") };
            var application = new Control { LayoutDirection = LayoutDirection.ApplicationLocale };
            var system = new Control { LayoutDirection = LayoutDirection.SystemLocale };
            var inherited = new Control();
            context.Add(application); context.Add(system); context.Add(inherited);

            Assert.That(application.IsLayoutRtl(), Is.True);
            Assert.That(system.IsLayoutRtl(), Is.False);
            Assert.That(inherited.IsLayoutRtl(), Is.True);
            context.RootLayoutDirection = LayoutDirection.LeftToRight;
            Assert.That(inherited.IsLayoutRtl(), Is.False);
        }

        [Test]
        public void Control_AnchoredResizeRequeuesItsOwnChildrenLikeGodotNotificationResized()
        {
            // Regression test: an anchor-driven resize previously bypassed QueueLayout entirely (it wrote
            // directly to the position/size backing fields), so a container that fills its parent via
            // anchors never re-arranged its OWN children when the parent resized. Godot's real
            // Container::_notification(NOTIFICATION_RESIZED) => queue_sort() re-sorts whenever a control's
            // own resolved size changes for any reason, not just through the Size property setter.
            var root = new Control { Size = new Vector2(200, 100) };
            var anchored = new HBoxContainer { Separation = 0 };
            anchored.SetAnchorsAndOffsets(0, 0, 1, 1);
            var first = new Control { HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand };
            var second = new Control { HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand };
            anchored.AddChild(first); anchored.AddChild(second);
            root.AddChild(anchored);
            var context = new UIContext(); context.Add(root); context.Layout();

            Assert.That(second.Bounds.X, Is.EqualTo(100), "Two equal expand children split the initial 200-wide root evenly.");

            root.Size = new Vector2(300, 100);
            context.Layout();

            Assert.That(anchored.Size, Is.EqualTo(new Vector2(300, 100)));
            Assert.That(second.Bounds.X, Is.EqualTo(150), "The anchored HBoxContainer must re-run ArrangeChildren for its own children after its anchor-resolved size changes, not just update its own rect.");
        }

        [Test]
        public void Control_AnchorResolvedSizeClampsToMinimumSizeWithGrowDirectionLikeGodotSizeChanged()
        {
            // Regression test: the anchor-resolution path never consulted GetMinimumSize() at all, so a
            // control's resolved size could shrink below its own stated minimum whenever the parent was
            // smaller than that minimum - unlike Godot's Control::_size_changed(), which always clamps up
            // to get_combined_minimum_size() and compensates position per GrowDirection (default End, i.e.
            // no compensation - the control simply grows past its right/bottom anchor edge).
            var parent = new Control { Size = new Vector2(100, 50) };
            var child = new Control { CustomMinimumSize = new Vector2(150, 20) };
            child.SetAnchorsAndOffsets(0, 0, 1, 1);
            parent.AddChild(child);
            var context = new UIContext(); context.Add(parent); context.Layout();

            Assert.That(child.Size.X, Is.EqualTo(150), "Size must clamp up to the minimum size, not shrink to the 100-wide parent.");
            Assert.That(child.Position.X, Is.EqualTo(0), "Default GrowDirection.End needs no position compensation.");

            child.HGrowDirection = GrowDirection.Begin;
            context.Layout();
            Assert.That(child.Position.X, Is.EqualTo(-50), "GrowDirection.Begin compensates fully from the left edge (size - minimum).");

            child.HGrowDirection = GrowDirection.Both;
            context.Layout();
            Assert.That(child.Position.X, Is.EqualTo(-25), "GrowDirection.Both compensates half from each edge.");
        }

        [Test]
        public void Control_SetAnchorDefaultPreservesPositionAndKeepOffsetAllowsItToJumpLikeGodot()
        {
            // Regression test: SetAnchor's keepOffset gate was inverted relative to Godot's real
            // Control::set_anchor - the default (keepOffset: false) must recompute the offset so the
            // resolved position does NOT jump when only the anchor changes, while keepOffset: true
            // explicitly leaves the offset untouched (letting the position jump).
            var parent = new Control { Size = new Vector2(200, 100) };
            var child = new Control();
            child.SetAnchorsAndOffsets(0, 0, 1, 1);
            parent.AddChild(child);

            child.SetAnchor(Side.Left, 0.5f);
            Assert.That(child.AnchorLeft, Is.EqualTo(0.5f));
            Assert.That(child.OffsetLeft, Is.EqualTo(-100f), "Default keepOffset:false must recompute the offset so the resolved left edge stays at its previous position (0).");

            child.SetAnchor(Side.Left, 0.75f, keepOffset: true);
            Assert.That(child.OffsetLeft, Is.EqualTo(-100f), "keepOffset:true must leave the offset untouched.");

            var context = new UIContext(); context.Add(parent); context.Layout();
            Assert.That(child.Position.X, Is.EqualTo(50f), "With the offset unchanged, the resolved position is now allowed to jump (0.75 * 200 - 100).");
        }

        [Test]
        public void Control_SetAnchorClampsOrPushesTheOppositeAnchorWhenCrossingLikeGodot()
        {
            // Regression test: SetAnchor never guarded against an anchor crossing its opposite anchor;
            // Godot's set_anchor either clamps the new anchor to the opposite (default) or pushes the
            // opposite anchor along when pushOppositeAnchor is requested.
            var parent = new Control { Size = new Vector2(200, 100) };
            var crossing = new Control();
            crossing.SetAnchorsAndOffsets(0.5f, 0, 0.5f, 1);
            parent.AddChild(crossing);

            crossing.SetAnchor(Side.Left, 0.8f);
            Assert.That(crossing.AnchorLeft, Is.EqualTo(0.5f), "Without pushOppositeAnchor, an anchor is clamped to its opposite instead of crossing it.");

            crossing.SetAnchor(Side.Left, 0.8f, pushOppositeAnchor: true);
            Assert.That(crossing.AnchorLeft, Is.EqualTo(0.8f));
            Assert.That(crossing.AnchorRight, Is.EqualTo(0.8f), "pushOppositeAnchor drags the opposite anchor along instead of clamping.");
        }

        [Test]
        public void Control_MirrorsAnchoredPositionUnderRtlEvenWithoutAContainerLikeGodotSizeChanged()
        {
            // Regression test: RTL mirroring for anchor-resolved rects was only ever applied by individual
            // containers/controls that manually checked IsLayoutRtl() themselves. Godot's real
            // Control::_size_changed() mirrors ANY anchored control horizontally under RTL as a base,
            // universal Control behavior - a plain anchored Control with no special RTL-aware container
            // parent should mirror too.
            var parent = new Control { Size = new Vector2(200, 100), LayoutDirection = LayoutDirection.RightToLeft };
            var child = new Control();
            child.SetAnchorsAndOffsets(0f, 0f, 0.5f, 1f);
            parent.AddChild(child);
            var context = new UIContext(); context.Add(parent); context.Layout();

            Assert.That(child.IsLayoutRtl(), Is.True);
            Assert.That(child.Size.X, Is.EqualTo(100));
            Assert.That(child.Position.X, Is.EqualTo(100), "Under RTL, a control anchored to the left half must mirror to the right half of its parent.");
        }

        [Test]
        public void Control_AcceptEventStopsPropagationForOnlyThatEventLikeGodot()
        {
            // Regression test: propagation up the ancestor chain was governed solely by the persistent
            // MouseFilter value, with no way for a control to dynamically consume a single event the way
            // Godot's Control::accept_event() does - e.g. Pass-filtered hover/pass-through behavior that
            // still wants to swallow one specific press without changing how future events propagate.
            var context = new UIContext();
            var parent = new InputProbe { Size = new Vector2(100, 100), MouseFilter = MouseFilter.Pass };
            var child = new AcceptingProbe { Size = new Vector2(50, 50), MouseFilter = MouseFilter.Pass };
            parent.AddChild(child);
            context.Add(parent);

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(child.PressedCount, Is.EqualTo(1));
            Assert.That(parent.PressedCount, Is.EqualTo(0), "AcceptEvent must stop propagation to the parent for this one event even though MouseFilter is Pass.");

            context.Update(new GameTime(TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16)), Mouse(10, 10, ButtonState.Released), new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromMilliseconds(32), TimeSpan.FromMilliseconds(16)), Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(child.PressedCount, Is.EqualTo(2));
            Assert.That(parent.PressedCount, Is.EqualTo(0), "A later, independent press must still be blocked - AcceptEvent must not permanently override MouseFilter.");
        }

        [Test]
        public void CenterContainer_UsesGodotTopLeftOriginModeAndMinimumSizing()
        {
            var center = new CenterContainer { Size = new Vector2(100, 60) };
            var child = new Control { CustomMinimumSize = new Vector2(40, 20) };
            center.AddChild(child);
            var context = new UIContext(); context.Add(center); context.Layout();

            Assert.That(center.GetMinimumSize(), Is.EqualTo(new Vector2(40, 20)));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(30, 20, 40, 20)));
            center.UseTopLeft = true; context.Layout();
            Assert.That(center.GetMinimumSize(), Is.EqualTo(Vector2.Zero));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(-20, -10, 40, 20)));
        }

        [Test]
        public void CenterContainer_FloorsTheCenteringOffsetLikeGodotVector2Floor()
        {
            // Godot's CenterContainer::_notification explicitly calls .floor() on the centering offset
            // (and on the top-left-mode offset, which floors a negative half-size toward -infinity too).
            // An odd size difference exercises this: a bare float division leaves a .5 fractional pixel.
            var center = new CenterContainer { Size = new Vector2(101, 61) };
            var child = new Control { CustomMinimumSize = new Vector2(40, 20) };
            center.AddChild(child);
            var context = new UIContext(); context.Add(center); context.Layout();
            Assert.That(child.Position, Is.EqualTo(new Vector2(30, 20)), "(101-40)/2=30.5 and (61-20)/2=20.5 must floor down to 30 and 20.");

            center.UseTopLeft = true;
            child.CustomMinimumSize = new Vector2(51, 21);
            context.Layout();
            Assert.That(child.Position, Is.EqualTo(new Vector2(-26, -11)), "-51/2=-25.5 and -21/2=-10.5 must floor toward negative infinity to -26 and -11, matching Vector2::floor.");
        }

        [Test]
        public void SubViewportContainer_UsesGodotIntegerStretchShrinkAndPointerMapping()
        {
            var texture = CreateHeadlessTexture(96, 48);
            var container = new SubViewportContainer { Position = new Vector2(50, 40), Size = new Vector2(200, 100), ViewportTexture = texture };

            Assert.That(container.GetMinimumSize(), Is.EqualTo(new Vector2(96, 48)));
            container.SetStretch(true);
            container.SetStretchShrink(2);
            container.SetMouseTarget(true);

            Assert.That(container.IsStretchEnabled(), Is.True);
            Assert.That(container.GetStretchShrink(), Is.EqualTo(2));
            Assert.That(container.IsMouseTargetEnabled(), Is.True);
            Assert.That(container.GetMinimumSize(), Is.EqualTo(Vector2.Zero), "Godot's stretched SubViewportContainer contributes no viewport-texture minimum size.");
            Assert.That(container.GetViewportSize(), Is.EqualTo(new Vector2(100, 50)));
            Assert.That(container.MapPointerToViewport(new Point(150, 140)), Is.EqualTo(new Vector2(50, 50)));
            Assert.Throws<ArgumentOutOfRangeException>(() => container.SetStretchShrink(0));
            Assert.That(container.GetStretchShrink(), Is.EqualTo(2), "Godot's ERR_FAIL_COND leaves the previous shrink value unchanged.");
        }

        [Test]
        public void SubViewportContainer_HostsIsolatedUiAndRoutesScaledPointerInput()
        {
            var viewport = new SubViewportContainer { Size = new Vector2(200, 100), Stretch = true, StretchShrink = 2 };
            var hostedButton = new Button { Position = new Vector2(20, 10), Size = new Vector2(40, 20) };
            viewport.ViewportContext.Add(hostedButton);
            var presses = 0;
            hostedButton.Pressed += (_, _) => presses++;
            var parentContext = new UIContext(); parentContext.Add(viewport);

            parentContext.Update(Time, Mouse(50, 30), new KeyboardState());
            parentContext.Update(Time, Mouse(50, 30, ButtonState.Pressed), new KeyboardState());
            parentContext.Update(Time, Mouse(50, 30), new KeyboardState());

            Assert.That(presses, Is.EqualTo(1));
            Assert.That(viewport.Children, Is.Empty, "Hosted roots remain isolated from the parent retained tree.");
            Assert.That(parentContext.FocusedControl, Is.SameAs(viewport));
            Assert.That(viewport.ViewportContext.FocusedControl, Is.SameAs(hostedButton));
            Assert.That(viewport.ViewportContext.ViewportSize, Is.EqualTo(new Vector2(100, 50)));
        }

        [Test]
        public void SubViewportContainer_ForwardsEveryPointerButtonToHostedUi()
        {
            var viewport = new SubViewportContainer { Size = new Vector2(200, 100), Stretch = true, StretchShrink = 2 };
            var hostedProbe = new PointerButtonProbe { Position = new Vector2(20, 10), Size = new Vector2(40, 20) };
            viewport.ViewportContext.Add(hostedProbe);
            var parentContext = new UIContext(); parentContext.Add(viewport);

            parentContext.Update(Time, Mouse(50, 30), new KeyboardState());
            parentContext.Update(Time, Mouse(50, 30, right: ButtonState.Pressed, middle: ButtonState.Pressed, xButton1: ButtonState.Pressed, xButton2: ButtonState.Pressed), new KeyboardState());
            parentContext.Update(Time, Mouse(50, 30), new KeyboardState());

            Assert.That(hostedProbe.PressedButtons, Is.EquivalentTo(new[] { PointerButton.Right, PointerButton.Middle, PointerButton.XButton1, PointerButton.XButton2 }));
            Assert.That(hostedProbe.ReleasedButtons, Is.EquivalentTo(new[] { PointerButton.Right, PointerButton.Middle, PointerButton.XButton1, PointerButton.XButton2 }));
        }

        [Test]
        public void VideoStreamPlayer_RetainsGodotLogicalPlaybackConfigurationWithoutAStream()
        {
            using var player = new VideoStreamPlayer();

            Assert.That(player.GetStream(), Is.Null);
            Assert.That(player.GetStreamName(), Is.EqualTo("<No Stream>"));
            Assert.That(player.GetStreamLength(), Is.Zero);
            Assert.That(player.GetStreamPosition(), Is.Zero);
            Assert.DoesNotThrow(() => player.SetStreamPosition(12), "Godot ignores seeks until a playback instance exists.");
            Assert.That(player.GetSpeedScale(), Is.EqualTo(1));
            Assert.That(player.GetBufferingMsec(), Is.EqualTo(500));
            Assert.That(player.GetAudioTrack(), Is.Zero);
            Assert.That(player.GetBus(), Is.EqualTo("Master"));
            Assert.That(player.IsPlaying(), Is.False);

            player.SetPaused(true);
            player.SetLoop(true);
            player.SetAutoplay(true);
            player.SetExpand(true);
            player.SetAudioTrack(3);
            player.SetBufferingMsec(750);
            player.SetBus("Dialogue");
            player.SetVolumeDb(-6);
            player.SetSpeedScale(1.5f);

            Assert.That(player.IsPaused(), Is.True, "Godot stores paused state even when no playback instance exists.");
            Assert.That(player.HasLoop(), Is.True);
            Assert.That(player.HasAutoplay(), Is.True);
            Assert.That(player.HasExpand(), Is.True);
            Assert.That(player.GetAudioTrack(), Is.EqualTo(3));
            Assert.That(player.GetBufferingMsec(), Is.EqualTo(750));
            Assert.That(player.GetBus(), Is.EqualTo("Dialogue"));
            Assert.That(player.GetVolume(), Is.EqualTo(MathF.Pow(10, -6 / 20f)).Within(0.0001f));
            Assert.That(player.GetVolumeDb(), Is.EqualTo(-6).Within(0.0001f));
            Assert.That(player.GetSpeedScale(), Is.EqualTo(1.5f));

            Assert.Throws<ArgumentOutOfRangeException>(() => player.SetSpeedScale(-1));
            Assert.That(player.GetSpeedScale(), Is.EqualTo(1.5f));
        }

        [Test]
        public void VideoStreamPlayer_DelegatesSeekingToTheOptionalBackend()
        {
            var backend = new VideoPlaybackBackendStub();
            var player = new VideoStreamPlayer(backend);

            player.SetStreamPosition(-12);

            Assert.That(backend.RequestedPosition, Is.EqualTo(TimeSpan.Zero));
            player.Dispose();
            Assert.That(backend.IsDisposed, Is.True);
        }

        [Test]
        public void VideoStreamPlayer_ReportsRuntimeCapabilities()
        {
            var runtime = typeof(VideoStreamPlayer).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "FormaRuntime").Value;
            var capabilities = VideoStreamPlayer.RuntimeCapabilities;

            Assert.That(capabilities.HasFlag(VideoPlaybackCapabilities.BuiltInPlayback), Is.True);
            Assert.That(capabilities.HasFlag(VideoPlaybackCapabilities.Looping), Is.True);
            Assert.That(capabilities.HasFlag(VideoPlaybackCapabilities.Audio), Is.True);
            Assert.That(capabilities.HasFlag(VideoPlaybackCapabilities.LocalFileLoading),
                Is.EqualTo(runtime == "FNA"));
            Assert.That(capabilities.HasFlag(VideoPlaybackCapabilities.Seeking), Is.False);
        }

        [Test]
        public void VideoStreamPlayer_ReportsAnUnavailableBackendWithoutThrowing()
        {
            var backend = new UnavailableVideoPlaybackBackendStub();
            using var player = new VideoStreamPlayer(backend)
            {
                Stream = CreateHeadlessVideo(),
            };

            Assert.DoesNotThrow(player.Play);
            Assert.That(player.IsPlaybackAvailable, Is.False);
            Assert.That(player.PlaybackUnavailableReason, Is.EqualTo("Video playback is unavailable."));
            Assert.That(player.PlaybackState, Is.EqualTo(MediaState.Stopped));
            Assert.That(backend.IsDisposed, Is.True);
        }

        [Test]
        public void VideoStreamPlayer_ForwardsCommonPlaybackStateAndConfiguration()
        {
            var backend = new VideoPlaybackBackendStub();
            using var player = new VideoStreamPlayer(backend)
            {
                Stream = CreateHeadlessVideo(),
                Loop = true,
                Volume = .4f,
            };

            player.Play();
            Assert.That(backend.PlayCount, Is.EqualTo(1));
            Assert.That(player.IsPlaying(), Is.True);
            Assert.That(backend.IsLooped, Is.True);
            Assert.That(backend.Volume, Is.EqualTo(.4f));

            player.SetPaused(true);
            Assert.That(player.PlaybackState, Is.EqualTo(MediaState.Paused));
            player.SetPaused(false);
            Assert.That(player.PlaybackState, Is.EqualTo(MediaState.Playing));
            player.Stop();
            Assert.That(player.PlaybackState, Is.EqualTo(MediaState.Stopped));
        }

        [Test]
        public void VideoStreamPlayer_RaisesFinishedOnceWhenPlaybackCompletes()
        {
            var backend = new VideoPlaybackBackendStub();
            using var player = new VideoStreamPlayer(backend)
            {
                Stream = CreateHeadlessVideo(),
            };
            var finishedCount = 0;
            player.Finished += (_, _) => finishedCount++;

            player.Play();
            backend.Complete();
            player.Process(Time);
            player.Process(Time);

            Assert.That(finishedCount, Is.EqualTo(1));
        }

        private static Video CreateHeadlessVideo()
        {
            var video = (Video)RuntimeHelpers.GetUninitializedObject(typeof(Video));
            GC.SuppressFinalize(video);
            return video;
        }

        private sealed class VideoPlaybackBackendStub : IVideoPlaybackBackend
        {
            public MediaState State { get; private set; } = MediaState.Stopped;
            public TimeSpan PlayPosition => RequestedPosition;
            public bool IsLooped { get; set; }
            public float Volume { get; set; }
            public int PlayCount { get; private set; }
            public TimeSpan RequestedPosition { get; private set; }
            public bool IsDisposed { get; private set; }
            public void Play(Video stream) { PlayCount++; State = MediaState.Playing; }
            public void Pause() => State = MediaState.Paused;
            public void Resume() => State = MediaState.Playing;
            public void Stop() => State = MediaState.Stopped;
            public void Complete() => State = MediaState.Stopped;
            public Texture2D GetTexture() => null;
            public bool TrySetPlayPosition(TimeSpan position)
            {
                RequestedPosition = position;
                return true;
            }
            public void Dispose() => IsDisposed = true;
        }

        private sealed class UnavailableVideoPlaybackBackendStub : IVideoPlaybackBackend
        {
            public MediaState State => MediaState.Stopped;
            public TimeSpan PlayPosition => TimeSpan.Zero;
            public bool IsLooped { get; set; }
            public float Volume { get; set; }
            public bool IsDisposed { get; private set; }
            public void Play(Video stream) => throw new PlatformNotSupportedException("Video playback is unavailable.");
            public void Pause() { }
            public void Resume() { }
            public void Stop() { }
            public Texture2D GetTexture() => null;
            public bool TrySetPlayPosition(TimeSpan position) => false;
            public void Dispose() => IsDisposed = true;
        }

        [Test]
        public void ScrollContainer_MapsGodotDisabledReserveAndMaximizeFirstModes()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 100), HorizontalScrollMode = ScrollBarVisibility.Reserve, VerticalScrollMode = ScrollBarVisibility.Disabled };
            scroll.AddChild(new Control { CustomMinimumSize = new Vector2(240, 240) });
            var context = new UIContext(); context.Add(scroll); context.Layout();

            Assert.That(scroll.HorizontalScrollBar.Visible, Is.True);
            Assert.That(scroll.VerticalScrollBar.Visible, Is.False);
            Assert.That(scroll.MaxScrollOffset, Is.EqualTo(new Vector2(140, 0)));
            scroll.ScrollOffset = new Vector2(80, 80);
            Assert.That(scroll.ScrollOffset, Is.EqualTo(new Vector2(80, 0)));
            scroll.HorizontalScrollMode = ScrollBarVisibility.MaximizeFirst;
            Assert.That(scroll.GetMinimumSize().X, Is.EqualTo(240));
        }

        [Test]
        public void TextureProgressBar_MapsGodotLinearBilinearAndRadialFillGeometry()
        {
            var bar = new TextureProgressBar { Size = new Vector2(100, 40), NinePatchStretch = true, Value = 25, FillMode = TextureProgressFillMode.RightToLeft };
            var rightToLeft = bar.GetProgressRegion(new Vector2(100, 40));
            bar.Value = 50; bar.SetFillMode(TextureProgressFillMode.BilinearTopAndBottom);
            var bilinear = bar.GetProgressRegion(new Vector2(100, 40));
            bar.SetFillMode(TextureProgressFillMode.Clockwise); bar.Value = 50; bar.SetRadialFillDegrees(180);
            bar.SetRadialInitialAngle(0);
            bar.SetRadialCenterOffset(new Vector2(4, -2));
            bar.SetTextureProgressOffset(new Vector2(3, 5));
            bar.SetTintUnder(Color.Red);
            bar.SetTintProgress(Color.Green);
            bar.SetTintOver(Color.Blue);
            bar.SetStretchMargin(Side.Left, 2);
            bar.SetStretchMargin(Side.Top, 3);
            bar.SetNinePatchStretch(true);
            var radial = bar.GetRadialFillPolygon(new Vector2(100, 40));

            Assert.That(rightToLeft.Destination, Is.EqualTo(new Rectangle(75, 0, 25, 40)));
            Assert.That(rightToLeft.Source, Is.EqualTo(new Rectangle(75, 0, 25, 40)));
            Assert.That(bilinear.Destination, Is.EqualTo(new Rectangle(0, 10, 100, 20)));
            Assert.That(bilinear.Source, Is.EqualTo(new Rectangle(0, 10, 100, 20)));
            Assert.That(bar.GetFillMode(), Is.EqualTo(TextureProgressFillMode.Clockwise));
            Assert.That(bar.GetRadialFillDegrees(), Is.EqualTo(180));
            Assert.That(bar.GetRadialInitialAngle(), Is.EqualTo(0));
            Assert.That(bar.GetRadialCenterOffset(), Is.EqualTo(new Vector2(4, -2)));
            Assert.That(bar.GetTextureProgressOffset(), Is.EqualTo(new Vector2(3, 5)));
            Assert.That(bar.GetTintUnder(), Is.EqualTo(Color.Red));
            Assert.That(bar.GetTintProgress(), Is.EqualTo(Color.Green));
            Assert.That(bar.GetTintOver(), Is.EqualTo(Color.Blue));
            Assert.That(bar.GetStretchMargin(Side.Left), Is.EqualTo(2));
            Assert.That(bar.GetStretchMargin(Side.Top), Is.EqualTo(3));
            Assert.That(bar.IsNinePatchStretchEnabled(), Is.True);
            // Godot's radial fan inserts an extra vertex at every 45/135/225/315-degree corner the sweep
            // crosses (floor(from*4+0.5)*0.25+0.125), not just the edge midpoints; this 0-to-90-degree
            // sweep (turns 0 to 0.25) crosses the 0.125 corner, producing three points, not two.
            // Godot's unit_val_to_uv clips in the TEXTURE's own unit-square UV space (not directly
            // against the possibly-differently-shaped display rect), then rescales per-axis afterward -
            // at exactly a 45-degree corner angle this always lands exactly on the UV-space corner
            // (1,0), which rescales to (100,0) here, not a point partway along the top edge.
            Assert.That(radial, Has.Count.EqualTo(3));
            Assert.That(radial[0].X, Is.EqualTo(54).Within(.001));
            Assert.That(radial[0].Y, Is.EqualTo(0).Within(.001));
            Assert.That(radial[1].X, Is.EqualTo(100).Within(.01));
            Assert.That(radial[1].Y, Is.EqualTo(0).Within(.01));
            Assert.That(radial[2].X, Is.EqualTo(100).Within(.001));
            Assert.That(radial[2].Y, Is.EqualTo(18).Within(.001));
            Assert.Throws<ArgumentOutOfRangeException>(() => bar.SetFillMode((TextureProgressFillMode)999));
            Assert.Throws<ArgumentOutOfRangeException>(() => bar.GetStretchMargin((Side)999));
        }

        [Test]
        public void TextureProgressBar_PreservesNinePatchCapsForAllNonRadialFillModes()
        {
            var bar = new TextureProgressBar { Size = new Vector2(100, 20), NinePatchStretch = true, Value = 50 };
            bar.SetStretchMargin(Side.Left, 5); bar.SetStretchMargin(Side.Top, 3);
            bar.SetStretchMargin(Side.Right, 7); bar.SetStretchMargin(Side.Bottom, 4);
            var textureSize = new Vector2(30, 12);

            bar.FillMode = TextureProgressFillMode.LeftToRight;
            var left = bar.GetNinePatchProgressRegion(textureSize);
            Assert.That(left.Destination, Is.EqualTo(new Rectangle(0, 0, 50, 20)));
            Assert.That(left.Source, Is.EqualTo(new Rectangle(0, 0, 14, 12)));
            Assert.That((left.Margins.Left, left.Margins.Right), Is.EqualTo((5, 0)));

            bar.FillMode = TextureProgressFillMode.RightToLeft;
            var right = bar.GetNinePatchProgressRegion(textureSize);
            Assert.That(right.Destination, Is.EqualTo(new Rectangle(50, 0, 50, 20)));
            Assert.That(right.Source, Is.EqualTo(new Rectangle(14, 0, 16, 12)));
            Assert.That((right.Margins.Left, right.Margins.Right), Is.EqualTo((0, 7)));

            bar.FillMode = TextureProgressFillMode.TopToBottom;
            var top = bar.GetNinePatchProgressRegion(textureSize);
            Assert.That(top.Destination, Is.EqualTo(new Rectangle(0, 0, 100, 10)));
            Assert.That(top.Source, Is.EqualTo(new Rectangle(0, 0, 30, 6)));
            Assert.That((top.Margins.Top, top.Margins.Bottom), Is.EqualTo((3, 0)));

            bar.FillMode = TextureProgressFillMode.BottomToTop;
            var bottom = bar.GetNinePatchProgressRegion(textureSize);
            Assert.That(bottom.Destination, Is.EqualTo(new Rectangle(0, 10, 100, 10)));
            Assert.That(bottom.Source, Is.EqualTo(new Rectangle(0, 6, 30, 6)));
            Assert.That((bottom.Margins.Top, bottom.Margins.Bottom), Is.EqualTo((0, 4)));

            bar.FillMode = TextureProgressFillMode.BilinearLeftAndRight;
            var horizontal = bar.GetNinePatchProgressRegion(textureSize);
            Assert.That(horizontal.Destination, Is.EqualTo(new Rectangle(25, 0, 50, 20)));
            Assert.That(horizontal.Source, Is.EqualTo(new Rectangle(9, 0, 10, 12)));
            Assert.That((horizontal.Margins.Left, horizontal.Margins.Right), Is.EqualTo((0, 0)));

            bar.FillMode = TextureProgressFillMode.BilinearTopAndBottom;
            var vertical = bar.GetNinePatchProgressRegion(textureSize);
            Assert.That(vertical.Destination, Is.EqualTo(new Rectangle(0, 5, 100, 10)));
            Assert.That(vertical.Source, Is.EqualTo(new Rectangle(0, 4, 30, 4)));
            Assert.That((vertical.Margins.Top, vertical.Margins.Bottom), Is.EqualTo((0, 0)));
        }

        [Test]
        public void TextureProgressBar_ReportsMinimumSizeFromStretchMarginsOrOnePixelFallbackLikeGodot()
        {
            // Godot's TextureProgressBar::get_minimum_size: nine-patch-stretch mode reports only the
            // stretch margins (a texture's own size never matters once it stretches); with no textures
            // and no nine-patch-stretch, the fallback is (1,1), not an arbitrary placeholder size.
            var stretched = new TextureProgressBar { NinePatchStretch = true };
            stretched.SetStretchMargin(Side.Left, 8); stretched.SetStretchMargin(Side.Right, 8);
            stretched.SetStretchMargin(Side.Top, 4); stretched.SetStretchMargin(Side.Bottom, 4);
            Assert.That(stretched.GetMinimumSize(), Is.EqualTo(new Vector2(16, 8)));

            var empty = new TextureProgressBar();
            Assert.That(empty.GetMinimumSize(), Is.EqualTo(Vector2.One));
        }

        [Test]
        public void TextureProgressBar_NormalizesRadialAngleAndClampsFillDegreesLikeGodotSetters()
        {
            var bar = new TextureProgressBar();
            bar.SetRadialInitialAngle(400);
            Assert.That(bar.GetRadialInitialAngle(), Is.EqualTo(40), "Godot's set_radial_initial_angle wraps an out-of-[0,360] angle via fposmodp.");
            bar.SetRadialInitialAngle(-30);
            Assert.That(bar.GetRadialInitialAngle(), Is.EqualTo(330), "fposmodp always returns a non-negative result.");

            bar.SetRadialFillDegrees(500);
            Assert.That(bar.GetRadialFillDegrees(), Is.EqualTo(360), "Godot's set_fill_degrees clamps into [0,360].");
            bar.SetRadialFillDegrees(-10);
            Assert.That(bar.GetRadialFillDegrees(), Is.EqualTo(0));
        }

        [Test]
        public void ZIndex_ControlsPaintingAndPointerPickingOrder()
        {
            var context = new UIContext();
            var root = new Panel { Size = new Vector2(100, 100) };
            var lower = new Button { Position = new Vector2(10, 10), Size = new Vector2(50, 30), ZIndex = 1 };
            var upper = new Button { Position = new Vector2(10, 10), Size = new Vector2(50, 30), ZIndex = 2 };
            root.AddChild(upper);
            root.AddChild(lower);
            context.Add(root);

            context.Update(Time, Mouse(20, 20), new KeyboardState());
            context.Update(Time, Mouse(20, 20, ButtonState.Pressed), new KeyboardState());

            Assert.That(context.FocusedControl, Is.SameAs(upper));
        }

        [Test]
        public void DragAndDrop_DeliversDataToAnAcceptingControl()
        {
            var context = new UIContext();
            var root = new Control { Size = new Vector2(200, 100) };
            var source = new DragSource { Position = new Vector2(10, 10), Size = new Vector2(50, 50) };
            var target = new DragTarget { Position = new Vector2(120, 10), Size = new Vector2(50, 50) };
            var dragEnded = false;
            source.DragEnded += (_, success) => dragEnded = success;
            root.AddChild(source); root.AddChild(target); context.Add(root);

            context.Update(Time, Mouse(20, 20), new KeyboardState());
            context.Update(Time, Mouse(20, 20, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(130, 20, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(130, 20), new KeyboardState());

            Assert.That(target.Received, Is.EqualTo("scene-node"));
            Assert.That(dragEnded, Is.True);
        }

        [Test]
        public void ExplicitFocusNeighbors_OverrideTreeTraversal()
        {
            var left = new Button();
            var right = new Button();
            left.FocusNeighborRight = right;
            right.FocusNeighborLeft = left;
            var context = new UIContext();
            context.Add(left); context.Add(right);
            left.GrabFocus();

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Right));

            Assert.That(context.FocusedControl, Is.SameAs(right));
        }

        [Test]
        public void MouseFilterPass_PropagatesEventsAndIgnoreDoesNotHideChildren()
        {
            var context = new UIContext();
            var parent = new InputProbe { Size = new Vector2(100, 100), MouseFilter = MouseFilter.Pass };
            var child = new InputProbe { Position = new Vector2(10, 10), Size = new Vector2(40, 40), MouseFilter = MouseFilter.Pass };
            parent.AddChild(child); context.Add(parent);
            context.Update(Time, Mouse(20, 20), new KeyboardState());
            context.Update(Time, Mouse(20, 20, ButtonState.Pressed), new KeyboardState());

            Assert.That(child.PressedCount, Is.EqualTo(1));
            Assert.That(parent.PressedCount, Is.EqualTo(1));
            parent.MouseFilter = MouseFilter.Ignore;
            Assert.That(context.HitTest(new Point(20, 20)), Is.SameAs(child));
        }

        [Test]
        public void AnchorsAndOffsets_ResolveAgainstParentSize()
        {
            var context = new UIContext();
            var root = new Control { Size = new Vector2(200, 100) };
            var child = new Control();
            child.SetAnchorsAndOffsets(.5f, .25f, .5f, .25f);
            child.SetOffset(Side.Left, 10); child.SetOffset(Side.Top, 5);
            child.SetOffset(Side.Right, 110); child.SetOffset(Side.Bottom, 25);
            root.AddChild(child); context.Add(root); context.Layout();

            Assert.That(child.Position, Is.EqualTo(new Vector2(110, 30)));
            Assert.That(child.Size, Is.EqualTo(new Vector2(100, 20)));
        }

        [Test]
        public void BoxContainer_DistributesExpandSpaceByStretchRatio()
        {
            var box = new HBoxContainer { Size = new Vector2(100, 20), Separation = 0 };
            var left = new Control { CustomMinimumSize = new Vector2(20, 10), HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand, SizeFlagsStretchRatio = 1 };
            var right = new Control { CustomMinimumSize = new Vector2(20, 10), HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand, SizeFlagsStretchRatio = 3 };
            box.AddChild(left); box.AddChild(right);
            var context = new UIContext(); context.Add(box); context.Layout();

            // Godot's BoxContainer::_resort divides the ENTIRE stretch_space (each expand child's own
            // minimum plus the leftover extra, combined) by ratio - it does not preserve each child's
            // minimum as a floor and split only the extra proportionally. With both children's minimum
            // (20) folded into a shared 100px pool split 1:3, that's 25/75, not a min-preserving 35/65.
            Assert.That(left.Size.X, Is.EqualTo(25).Within(.001));
            Assert.That(right.Size.X, Is.EqualTo(75).Within(.001));
            Assert.That(right.Position.X, Is.EqualTo(25).Within(.001));
        }

        [Test]
        public void BoxContainer_AZeroStretchRatioChildGetsExactlyItsMinimumLikeGodotSetStretchRatio()
        {
            // Godot's Control::set_stretch_ratio performs no clamping (control.cpp:2343-2355), so a
            // true zero ratio is valid: BoxContainer::_resort computes that child's share as
            // stretch_space * 0 / total = 0, which is below its own minimum, so it is immediately
            // pinned to its minimum and dropped from the pool - it receives none of the extra space.
            var box = new HBoxContainer { Size = new Vector2(100, 20), Separation = 0 };
            var left = new Control { CustomMinimumSize = new Vector2(10, 10), HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand, SizeFlagsStretchRatio = 0 };
            var right = new Control { CustomMinimumSize = new Vector2(10, 10), HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand, SizeFlagsStretchRatio = 1 };
            box.AddChild(left); box.AddChild(right);
            var context = new UIContext(); context.Add(box); context.Layout();

            Assert.That(left.Size.X, Is.EqualTo(10).Within(.001), "A zero ratio must not receive even a sliver of extra space.");
            Assert.That(right.Size.X, Is.EqualTo(90).Within(.001));
        }

        [Test]
        public void LineEdit_ProvidesDeterministicTextEditingApi()
        {
            var edit = new LineEdit();
            edit.InsertText("axc");
            edit.DeleteText(1, 2);

            Assert.That(edit.Text, Is.EqualTo("ac"));
            edit.InsertText("b");
            Assert.That(edit.Text, Is.EqualTo("abc"));
        }

        [Test]
        public void LineEdit_InitialBoundTextPlacesCaretAtTheEnd()
        {
            var edit = new LineEdit { Text = "Tracer" };

            Assert.That(edit.CaretColumn, Is.EqualTo(edit.Text.Length));
            edit.InsertText("X");
            Assert.That(edit.Text, Is.EqualTo("TracerX"));
        }

        [Test]
        public void LineEdit_SelectAllOnFocusSurvivesTheFocusingMouseClickInsteadOfCollapsingToACaret()
        {
            var edit = new LineEdit { Text = "hello", SelectAllOnFocus = true, Size = new Vector2(160, 24) };
            var context = new UIContext(); context.Add(edit);

            context.Update(Time, Mouse(20, 10), new KeyboardState());
            context.Update(Time, Mouse(20, 10, ButtonState.Pressed), new KeyboardState());

            Assert.That(edit.HasSelection, Is.True);
            Assert.That(edit.SelectedText, Is.EqualTo("hello"));
        }

        [Test]
        public void LineEdit_MouseDragSelectsTheCapturedCharacterRange()
        {
            var edit = new LineEdit { Font = CreateTestFont(), Text = "hello world", Size = new Vector2(160, 24) };
            var context = new UIContext(); context.Add(edit);

            context.Update(Time, Mouse(14, 10), new KeyboardState());
            context.Update(Time, Mouse(14, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(46, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(46, 10), new KeyboardState());

            Assert.That(edit.SelectionFrom, Is.EqualTo(1));
            Assert.That(edit.SelectionTo, Is.EqualTo(5));
            Assert.That(edit.SelectedText, Is.EqualTo("ello"));

            edit.SelectingEnabled = false;
            Assert.That(edit.HasSelection, Is.False);
            Assert.That(edit.IsSelectingEnabled(), Is.False);
        }

        [Test]
        public void LineEdit_DoubleClickSelectsAWordAndDraggingExtendsByWholeWords()
        {
            var edit = new LineEdit { Font = CreateTestFont(), Text = "one two three", Size = new Vector2(180, 24) };
            var context = new UIContext(); context.Add(edit);

            context.Update(Time, Mouse(14, 10), new KeyboardState());
            context.Update(Time, Mouse(14, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(14, 10), new KeyboardState());
            context.Update(Time, Mouse(14, 10, ButtonState.Pressed), new KeyboardState());

            Assert.That(edit.SelectedText, Is.EqualTo("one"));

            context.Update(Time, Mouse(86, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(86, 10), new KeyboardState());

            Assert.That(edit.SelectedText, Is.EqualTo("one two three"));
        }

        [Test]
        public void LineEdit_TripleClickSelectsAllAndTheMultiClickSequenceExpires()
        {
            var edit = new LineEdit { Font = CreateTestFont(), Text = "one two", Size = new Vector2(160, 24) };
            var context = new UIContext(); context.Add(edit);

            for (var click = 0; click < 3; click++)
            {
                context.Update(Time, Mouse(14, 10, ButtonState.Pressed), new KeyboardState());
                context.Update(Time, Mouse(14, 10), new KeyboardState());
            }

            Assert.That(edit.SelectedText, Is.EqualTo("one two"));

            var later = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            context.Update(later, Mouse(14, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(later, Mouse(14, 10), new KeyboardState());

            Assert.That(edit.HasSelection, Is.False, "A click after Godot's 600 ms grouping window starts a new sequence.");
            Assert.That(edit.CaretColumn, Is.EqualTo(1));
        }

        [Test]
        public void LineEdit_ShiftArrowsAndShiftHomeEndExtendSelectionLikeGodotsShiftSelectionCheck()
        {
            var edit = new LineEdit { Text = "hello world", Size = new Vector2(160, 24) };
            var context = new UIContext(); context.Add(edit); edit.GrabFocus();
            edit.Select(5, 5);

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Right));
            Assert.That(edit.SelectionFrom, Is.EqualTo(5));
            Assert.That(edit.SelectionTo, Is.EqualTo(6));

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Right, Keys.End));
            Assert.That(edit.SelectedText, Is.EqualTo(" world"));

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Left));
            Assert.That(edit.HasSelection, Is.False, "A plain arrow key without shift should collapse the selection like Godot's non-select move.");
            Assert.That(edit.CaretColumn, Is.EqualTo(5), "Left with no shift while a selection is active collapses to the selection's start.");
        }

        [Test]
        public void LineEdit_CutFallsBackToCopyWhenNotEditableLikeGodotsUiCut()
        {
            var edit = new LineEdit { Text = "hello", Editable = false };
            using var context = new UIContext(new TestClipboard()); context.Add(edit);
            edit.Select(0, 5);
            var copied = string.Empty;
            edit.CopyRequested += (_, text) => copied = text;

            edit.Cut();

            Assert.That(copied, Is.EqualTo("hello"));
            Assert.That(edit.Text, Is.EqualTo("hello"), "A read-only field must not have its text removed by Cut.");
        }

        [Test]
        public void LineEdit_ShrinkingMaxLengthRetroactivelyTruncatesTextAndFiresTextChangeRejected()
        {
            var edit = new LineEdit { Text = "abcdef" };
            var rejected = string.Empty;
            edit.TextChangeRejected += (_, dropped) => rejected = dropped;

            edit.MaxLength = 3;

            Assert.That(edit.Text, Is.EqualTo("abc"));
            Assert.That(rejected, Is.EqualTo("def"));
        }

        [Test]
        public void LineEdit_InsertTextRejectsTheOverflowingTailOnceMaxLengthIsSet()
        {
            var edit = new LineEdit { MaxLength = 5, Text = "abc" };
            edit.Select(3, 3);
            var rejected = string.Empty;
            edit.TextChangeRejected += (_, dropped) => rejected = dropped;

            edit.InsertText("xyz");

            Assert.That(edit.Text, Is.EqualTo("abcxy"));
            Assert.That(rejected, Is.EqualTo("z"));
        }

        [Test]
        public void LineEdit_PlatformWordModifierNavigatesAndDeletesWholeWords()
        {
            var edit = new LineEdit { Text = "alpha beta gamma", Size = new Vector2(200, 24) };
            var context = new UIContext(); context.Add(edit); edit.GrabFocus();
            edit.Select(0, 0);
            var modifier = OperatingSystem.IsMacOS() ? Keys.LeftAlt : Keys.LeftControl;

            context.Update(Time, Mouse(0, 0), new KeyboardState(modifier));
            context.Update(Time, Mouse(0, 0), new KeyboardState(modifier, Keys.Right));
            Assert.That(edit.CaretColumn, Is.EqualTo(5), "The word modifier should land at the end of 'alpha'.");

            context.Update(Time, Mouse(0, 0), new KeyboardState(modifier));
            context.Update(Time, Mouse(0, 0), new KeyboardState(modifier, Keys.Right));
            Assert.That(edit.CaretColumn, Is.EqualTo(10), "The word modifier should skip the space then land at the end of 'beta'.");

            context.Update(Time, Mouse(0, 0), new KeyboardState(modifier));
            context.Update(Time, Mouse(0, 0), new KeyboardState(modifier, Keys.Left));
            Assert.That(edit.CaretColumn, Is.EqualTo(6), "The word modifier should land at the start of 'beta'.");

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            edit.Select(11, 11);
            context.Update(Time, Mouse(0, 0), new KeyboardState(modifier));
            context.Update(Time, Mouse(0, 0), new KeyboardState(modifier, Keys.Back));
            Assert.That(edit.Text, Is.EqualTo("alpha gamma"), "The word modifier should delete the whole preceding word 'beta'.");
        }

        [Test]
        public void TextEditors_ReplaceSelectionAndNavigateMultilineCarets()
        {
            var lineEdit = new LineEdit { Text = "alpha" };
            lineEdit.Select(1, 4);
            lineEdit.InsertText("Z");
            var textEdit = new TextEdit { Text = "one\nlonger\ntwo" };
            textEdit.SetCaret(1, 4);
            textEdit.KeyPressed(Keys.Down);

            Assert.That(lineEdit.Text, Is.EqualTo("aZa"));
            Assert.That(lineEdit.HasSelection, Is.False);
            Assert.That(textEdit.CaretLine, Is.EqualTo(2));
            Assert.That(textEdit.CaretColumnInLine, Is.EqualTo(3));
        }

        [Test]
        public void TextEdit_TracksGodotLineHistorySearchAndViewportState()
        {
            var edit = new TextEdit { Text = "alpha beta\nbeta" };
            edit.ClearUndoHistory();
            edit.SetLine(0, "omega beta");
            edit.SetLineBackgroundColor(1, Color.Orange);
            edit.InsertLineAt(1, "inserted");
            edit.RemoveLineAt(1);
            var forward = edit.Search("beta", TextSearchFlags.WholeWords);
            var backwards = edit.Search("beta", TextSearchFlags.MatchCase | TextSearchFlags.Backwards, 1, 4);
            edit.Undo();
            edit.Redo();
            edit.SetLineAsLastVisible(1);

            Assert.That(edit.GetLine(0), Is.EqualTo("omega beta"));
            Assert.That(edit.LineCount, Is.EqualTo(2));
            Assert.That(edit.GetLineBackgroundColor(1), Is.EqualTo(Color.Orange));
            Assert.That(forward, Is.EqualTo(new Point(6, 0)));
            Assert.That(backwards, Is.EqualTo(new Point(0, 1)));
            Assert.That(edit.HasUndo, Is.True);
            Assert.That(edit.HasRedo, Is.False);
            Assert.That(edit.FirstVisibleLine, Is.EqualTo(1));
        }

        [Test]
        public void TextEdit_MapsGodotTypedGuttersAndPreservesLineItemsAcrossEdits()
        {
            var edit = new TextEdit { Text = "first\nsecond", Size = new Vector2(200, 80) };
            edit.AddGutter();
            edit.SetGutterName(0, "State"); edit.SetGutterType(0, TextEditGutterType.String); edit.SetGutterWidth(0, 36); edit.SetGutterClickable(0, true);
            edit.SetLineGutterText(0, 0, "run"); edit.SetLineGutterMetadata(0, 0, "entry"); edit.SetLineGutterItemColor(0, 0, Color.CornflowerBlue); edit.SetLineGutterClickable(0, 0, true);

            Assert.That(edit.GutterCount, Is.EqualTo(1));
            Assert.That(edit.GetGutterName(0), Is.EqualTo("State"));
            Assert.That(edit.GetTotalGutterWidth(), Is.EqualTo(36));
            Assert.That(edit.GetLineGutterText(0, 0), Is.EqualTo("run"));
            Assert.That(edit.GetLineGutterMetadata(0, 0), Is.EqualTo("entry"));
            Assert.That(edit.GetLineGutterItemColor(0, 0), Is.EqualTo(Color.CornflowerBlue));
            Assert.That(edit.IsLineGutterClickable(0, 0), Is.True);
            edit.InsertLineAt(0, "before");
            Assert.That(edit.GetLineGutterMetadata(1, 0), Is.EqualTo("entry"));
            edit.RemoveLineAt(0);
            Assert.That(edit.GetLineGutterText(0, 0), Is.EqualTo("run"));
            edit.RemoveGutter(0);
            Assert.That(edit.GutterCount, Is.EqualTo(0));
        }

        [Test]
        public void TextEdit_MapsGodotViewportQueriesAndCaretAdjustment()
        {
            var edit = new TextEdit { Text = "0\n1\n2\n3\n4\n5\n6\n7", Size = new Vector2(120, 32) };
            edit.SetCaret(6, 0);
            edit.AdjustViewportToCaret();

            Assert.That(edit.GetVisibleLineCount(), Is.EqualTo(2));
            Assert.That(edit.FirstVisibleLine, Is.EqualTo(5));
            Assert.That(edit.GetLastFullVisibleLine(), Is.EqualTo(6));
            Assert.That(edit.IsLineInViewport(6), Is.True);
            Assert.That(edit.GetVisibleLineCountInRange(2, 5), Is.EqualTo(4));
            Assert.That(edit.GetTotalVisibleLineCount(), Is.EqualTo(8));
            edit.CenterViewportToCaret();
            Assert.That(edit.FirstVisibleLine, Is.EqualTo(5));
            edit.SetCaret(1, 0); edit.AdjustViewportToCaret();
            Assert.That(edit.FirstVisibleLine, Is.EqualTo(1));
        }

        [TestCase(typeof(TextEdit))]
        [TestCase(typeof(CodeEdit))]
        public void MultilineEditorsKeepClickedCaretLineAfterPointerRelease(Type editorType)
        {
            var editor = (TextEdit)Activator.CreateInstance(editorType)!;
            editor.Font = CreateTestFont();
            editor.Text = "first\nsecond\nthird";
            editor.Size = new Vector2(240, 64);
            using var context = new UIContext();
            context.Add(editor);

            var point = new Point(100, 24);
            context.Update(Time, Mouse(point.X, point.Y), new KeyboardState());
            context.Update(Time, Mouse(point.X, point.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(point.X, point.Y), new KeyboardState());

            Assert.That(editor.CaretLine, Is.EqualTo(1));
            Assert.That(editor.CaretColumnInLine, Is.GreaterThan(0));

            var thirdLinePoint = new Point(point.X, 40);
            context.Update(Time, Mouse(point.X, point.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(thirdLinePoint.X, thirdLinePoint.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(thirdLinePoint.X, thirdLinePoint.Y), new KeyboardState());

            Assert.That(editor.CaretLine, Is.EqualTo(2));
            Assert.That(editor.HasSelection, Is.True);
        }

        [Test]
        public void TabContainer_ShowsOnlyTheSelectedPage()
        {
            var tabs = new TabContainer { Size = new Vector2(100, 100) };
            var first = new Control(); var second = new Control();
            tabs.AddChild(first); tabs.AddChild(second);
            var context = new UIContext(); context.Add(tabs); context.Layout();
            tabs.CurrentTab = 1;

            Assert.That(first.Visible, Is.False);
            Assert.That(second.Visible, Is.True);
        }

        [Test]
        public void TabContainer_ReportsMinimumSizeFromTabHeightAndCurrentPageLikeGodot()
        {
            // Godot's TabContainer::_get_minimum_size adds the tab-bar height to only the CURRENT
            // page's minimum size by default (use_hidden_tabs_for_min_size, which this port doesn't
            // model, is what would fold in every page instead), plus the popup button's width when one
            // is attached.
            var tabs = new TabContainer { TabHeight = 28 };
            var first = new Control { CustomMinimumSize = new Vector2(120, 60) };
            var second = new Control { CustomMinimumSize = new Vector2(50, 200) };
            tabs.AddChild(first); tabs.AddChild(second);

            Assert.That(tabs.GetMinimumSize(), Is.EqualTo(new Vector2(136, 104)), "Only the current (first) page's minimum plus the body inset should count, not the larger second page.");

            tabs.CurrentTab = 1;
            Assert.That(tabs.GetMinimumSize(), Is.EqualTo(new Vector2(66, 244)), "Switching the current tab changes which page's minimum is folded in.");

            tabs.SetPopup(new Popup());
            Assert.That(tabs.GetMinimumSize().X, Is.EqualTo(86), "An attached popup button adds its width to the inset content minimum, matching Godot's popup_button branch.");
        }

        [Test]
        public void TabContainer_DefaultHeaderHeightFitsActiveFontWithBalancedPadding()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 32);
            var page = new Control { CustomMinimumSize = new Vector2(120, 60) };
            var tabs = new TabContainer { UIFont = font, Size = new Vector2(200, 120) };
            tabs.AddChild(page);
            var context = new UIContext();
            context.Add(tabs);
            context.Layout();

            var expectedHeaderHeight = MathF.Ceiling(Math.Max(28, TextMetrics.LineHeight(font) + 10));
            Assert.That(tabs.EffectiveTabHeight, Is.EqualTo(expectedHeaderHeight));
            Assert.That(tabs.GetMinimumSize().Y, Is.EqualTo(expectedHeaderHeight + 76));
            Assert.That(page.Position, Is.EqualTo(new Vector2(8, expectedHeaderHeight + 8)));
            Assert.That(page.Size, Is.EqualTo(new Vector2(184, 120 - expectedHeaderHeight - 16)));
        }

        [Test]
        public void TabContainer_CentersVisibleTitleGlyphsWithinHeader()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var layout = TextMetrics.Layout(new DynamicUIFont(face, 24), "General");
            var header = new Rectangle(0, 10, 200, 39);
            var titleY = TabContainer.GetTabTitleY(layout, header);
            var visibleTop = layout.VisibleGlyphs.Min(glyph => glyph.Bounds.Top) + titleY;
            var visibleBottom = layout.VisibleGlyphs.Max(glyph => glyph.Bounds.Bottom) + titleY;

            Assert.That(visibleTop - header.Top, Is.EqualTo(header.Bottom - visibleBottom).Within(.001f));
        }

        [Test]
        public void TabContainer_SelectedTabGetsDefaultAccentIndicator()
        {
            var tabs = new TabContainer { Size = new Vector2(200, 100), DeselectEnabled = true };
            tabs.AddChild(new Control());
            tabs.AddChild(new Control());
            using var context = new UIContext();
            context.Add(tabs);
            context.Layout();

            tabs.CurrentTab = 1;
            Assert.That(tabs.GetSelectedTabRectangle(), Is.EqualTo(new Rectangle(100, -3, 100, 31)));
            Assert.That(tabs.GetSelectedTabIndicatorRectangle(), Is.EqualTo(new Rectangle(101, -3, 98, 3)));

            context.Theme.TabSelectedLift = 5;
            context.Theme.TabSelectedIndicatorHeight = 2;
            Assert.That(tabs.GetSelectedTabRectangle(), Is.EqualTo(new Rectangle(100, -5, 100, 33)));
            Assert.That(tabs.GetSelectedTabIndicatorRectangle(), Is.EqualTo(new Rectangle(101, -5, 98, 2)));

            tabs.CurrentTab = -1;
            Assert.That(tabs.GetSelectedTabRectangle(), Is.EqualTo(Rectangle.Empty));
            Assert.That(tabs.GetSelectedTabIndicatorRectangle(), Is.EqualTo(Rectangle.Empty));
        }

        [Test]
        public void TabContainer_PreservesChildNamesAsTabTitles()
        {
            var tabs = new TabContainer();
            tabs.AddChild(new Control { Name = "Viewport" });
            tabs.AddChild(new Control { Name = "Inspector" });

            Assert.That(tabs.Children[0].Name, Is.EqualTo("Viewport"));
            Assert.That(tabs.Children[1].Name, Is.EqualTo("Inspector"));
        }

        [Test]
        public void TabContainer_SupportsDeselectionAndTracksPreviousTabLikeGodot()
        {
            var container = new TabContainer { DeselectEnabled = true, Size = new Vector2(200, 100) };
            var a = new Control(); var b = new Control();
            container.AddChild(a); container.AddChild(b);
            var context = new UIContext(); context.Add(container); context.Layout();

            container.CurrentTab = 1;
            Assert.That(container.GetPreviousTab(), Is.EqualTo(0));

            container.CurrentTab = -1;
            Assert.That(container.CurrentTab, Is.EqualTo(-1));
            Assert.That(a.Visible, Is.False);
            Assert.That(b.Visible, Is.False);
            Assert.That(container.GetPreviousTab(), Is.EqualTo(1));
        }

        [Test]
        public void TabContainer_ClicksFireTabClickedAndBlockDisabledTabsLikeGodot()
        {
            var container = new TabContainer { Size = new Vector2(200, 60) };
            container.AddChild(new Control()); container.AddChild(new Control());
            container.SetTabDisabled(1, true);
            var clicked = new List<int>();
            container.TabClicked += (_, index) => clicked.Add(index);
            var context = new UIContext(); context.Add(container); context.Layout();

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(container.CurrentTab, Is.EqualTo(0));
            Assert.That(clicked, Is.EqualTo(new[] { 0 }));
            context.Update(Time, Mouse(150, 10), new KeyboardState());
            context.Update(Time, Mouse(150, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(container.CurrentTab, Is.EqualTo(0), "Clicking a disabled tab must not select it or fire tab_clicked, matching Godot's gui_input gate.");
            Assert.That(clicked, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void TabContainer_ForwardsTabHoverAndButtonIconPressWithoutSelecting()
        {
            var buttonIcon = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var tabs = new TabContainer { Size = new Vector2(200, 100) };
            tabs.AddChild(new Control { Name = "Scene" });
            tabs.AddChild(new Control { Name = "Inspector" });
            tabs.CurrentTab = 1;
            tabs.SetTabButtonIcon(0, buttonIcon);
            var hovered = new List<int>(); var buttonPressed = new List<int>(); var clicked = new List<int>();
            tabs.TabHovered += (_, tab) => hovered.Add(tab);
            tabs.TabButtonPressed += (_, tab) => buttonPressed.Add(tab);
            tabs.TabClicked += (_, tab) => clicked.Add(tab);
            var context = new UIContext(); context.Add(tabs); context.Layout();

            context.Update(Time, Mouse(90, 14), new KeyboardState());
            context.Update(Time, Mouse(90, 14, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(90, 14), new KeyboardState());
            context.Update(Time, Mouse(150, 14), new KeyboardState());

            Assert.That(hovered, Is.EqualTo(new[] { 0, 1 }), "Moving between headers without capture must emit tab_hovered for each entered tab.");
            Assert.That(buttonPressed, Is.EqualTo(new[] { 0 }));
            Assert.That(clicked, Is.Empty);
            Assert.That(tabs.CurrentTab, Is.EqualTo(1));
        }

        [Test]
        public void TabContainer_GetTabControlAndGetCurrentTabControlLikeGodot()
        {
            var container = new TabContainer { Size = new Vector2(200, 100) };
            var a = new Control(); var b = new Control();
            container.AddChild(a); container.AddChild(b);
            var context = new UIContext(); context.Add(container); context.Layout();

            Assert.That(container.GetTabControl(0), Is.SameAs(a));
            Assert.That(container.GetTabControl(1), Is.SameAs(b));
            Assert.That(container.GetCurrentTabControl(), Is.SameAs(a));

            container.CurrentTab = 1;
            Assert.That(container.GetCurrentTabControl(), Is.SameAs(b));
        }

        [Test]
        public void TabContainer_SelectPreviousNextAvailableReturnsWhetherATabWasFoundLikeGodot()
        {
            var container = new TabContainer { Size = new Vector2(200, 100) };
            container.AddChild(new Control()); container.AddChild(new Control());
            container.SetTabDisabled(1, true);
            var context = new UIContext(); context.Add(container); context.Layout();

            Assert.That(container.SelectNextAvailable(), Is.False, "The only other tab is disabled, so no wraparound target is available, matching Godot's get_next_available never re-considering the current tab.");
            Assert.That(container.CurrentTab, Is.EqualTo(0));

            container.SetTabDisabled(1, false);
            Assert.That(container.SelectNextAvailable(), Is.True);
            Assert.That(container.CurrentTab, Is.EqualTo(1));
            Assert.That(container.SelectPreviousAvailable(), Is.True);
            Assert.That(container.CurrentTab, Is.EqualTo(0));
        }

        [Test]
        public void TabContainer_MapsGodotPopupButtonPressPositioningAndPrePopupSignal()
        {
            var tabs = new TabContainer { Size = new Vector2(100, 60) };
            tabs.AddChild(new Control { Name = "Scene" });
            tabs.AddChild(new Control { Name = "Script" });
            var context = new UIContext(); context.Add(tabs); context.Layout();

            Assert.That(tabs.GetPopup(), Is.Null);
            Assert.That(tabs.GetPopupButtonRectangle(), Is.EqualTo(Rectangle.Empty), "No popup button reserved space until a popup is attached.");

            var popup = new Popup { Size = new Vector2(60, 30) };
            var prePopupCount = 0; tabs.PrePopupPressed += (_, _) => prePopupCount++;
            tabs.SetPopup(popup);
            Assert.That(tabs.GetPopup(), Is.SameAs(popup));

            var buttonRect = tabs.GetPopupButtonRectangle();
            Assert.That(buttonRect, Is.EqualTo(new Rectangle(80, 0, 20, 28)), "Godot reserves the popup button at the trailing edge of the header in LTR.");

            context.Update(Time, Mouse(buttonRect.Center.X, buttonRect.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(buttonRect.Center.X, buttonRect.Center.Y), new KeyboardState());

            Assert.That(prePopupCount, Is.EqualTo(1), "Godot emits pre_popup_pressed before positioning and showing the popup.");
            Assert.That(popup.Visible, Is.True);
            Assert.That(popup.Context, Is.SameAs(context), "TabContainer adds an unparented popup to its own context, matching Godot's popup() call on a free-floating node.");
            Assert.That(popup.Position, Is.EqualTo(new Vector2(40, 28)), "x right-aligns the popup's trailing edge with the button; y sits just below the header.");

            popup.Hide();
            tabs.SetPopup(null);
            Assert.That(tabs.GetPopupButtonRectangle(), Is.EqualTo(Rectangle.Empty), "Detaching the popup frees the reserved header space.");
        }

        [Test]
        public void PopupMenu_ActivatesCheckItemAndCloses()
        {
            var menu = new PopupMenu();
            var check = menu.AddCheckItem("Snap", 42);
            var pressedId = -1;
            menu.IdPressed += (_, id) => pressedId = id;
            var context = new UIContext();
            context.Add(menu);
            menu.PopupAt(Vector2.Zero, new Vector2(120, 0));

            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(8, 8), new KeyboardState());

            Assert.That(check.Checked, Is.False, "Godot activation emits the item but does not toggle check state implicitly.");
            Assert.That(menu.IsItemCheckable(0), Is.True);
            Assert.That(pressedId, Is.EqualTo(42));
            Assert.That(menu.Visible, Is.False);
        }

        [Test]
        public void PopupMenuItems_IsTheHitTestSurfaceForItsMenu()
        {
            var menu = new PopupMenu();
            menu.AddItem("Open");
            var context = new UIContext(); context.Add(menu);
            menu.PopupAt(Vector2.Zero, new Vector2(120, 0));
            context.Layout();

            Assert.That(context.HitTest(new Point(8, 8)), Is.SameAs(menu.ItemsControl));
        }

        [Test]
        public void Popup_ModalOutsideClickDismissesWithoutClickingThrough()
        {
            var context = new UIContext();
            var button = new Button { Size = new Vector2(100, 40) };
            var popup = new Popup { Size = new Vector2(40, 30), HideOnOutsideClick = true };
            var clicks = 0;
            PopupHideReason? hiddenReason = null;
            button.Pressed += (_, _) => clicks++;
            popup.PopupHidden += (_, reason) => hiddenReason = reason;
            context.Add(button); context.Add(popup); popup.PopupAt(new Vector2(50, 50));

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());

            Assert.That(popup.Visible, Is.False);
            Assert.That(hiddenReason, Is.EqualTo(PopupHideReason.OutsideClick));
            Assert.That(clicks, Is.EqualTo(0));
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            Assert.That(clicks, Is.EqualTo(1));
        }

        [Test]
        public void Popup_ExclusiveBlocksUnderlyingInputWithoutOutsideDismissal()
        {
            var context = new UIContext();
            var button = new Button { Size = new Vector2(100, 40) };
            var popup = new Popup { Size = new Vector2(40, 30), Exclusive = true };
            var clicks = 0; button.Pressed += (_, _) => clicks++;
            context.Add(button); context.Add(popup); popup.PopupAt(new Vector2(50, 50));

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());

            Assert.That(popup.Visible, Is.True);
            Assert.That(clicks, Is.EqualTo(0));
        }

        [Test]
        public void PopupMenu_CheckItemsCanRemainOpenAfterSelection()
        {
            var menu = new PopupMenu { HideOnCheckableItemSelection = false };
            var check = menu.AddCheckItem("Snap");
            var context = new UIContext(); context.Add(menu); menu.PopupAt(Vector2.Zero, new Vector2(120, 0));

            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(8, 8), new KeyboardState());

            Assert.That(check.Checked, Is.False);
            Assert.That(menu.Visible, Is.True);
        }

        [Test]
        public void PopupMenu_MapsGodotExplicitCheckableAndRadioStateApis()
        {
            var menu = new PopupMenu();
            menu.AddItem("Plain", 1);
            menu.AddCheckItem("Snap", 2);
            menu.AddRadioCheckItem("Mode A", 3);
            menu.AddRadioCheckItem("Mode B", 4);

            Assert.That(menu.IsHideOnCheckableItemSelection(), Is.True);
            menu.SetHideOnCheckableItemSelection(false);
            Assert.That(menu.IsHideOnCheckableItemSelection(), Is.False);
            Assert.That(menu.IsItemCheckable(0), Is.False);
            Assert.That(menu.IsItemCheckable(1), Is.True);
            Assert.That(menu.IsItemRadioCheckable(1), Is.False);
            Assert.That(menu.IsItemRadioCheckable(2), Is.True);

            menu.SetItemChecked(1, true);
            menu.SetItemChecked(-1, true);
            Assert.That(menu.IsItemChecked(1), Is.True);
            Assert.That(menu.IsItemChecked(3), Is.True);
            menu.SetItemChecked(-1, false);
            Assert.That(menu.IsItemChecked(3), Is.False);
            menu.SetItemIndeterminate(1, true);
            Assert.That(menu.IsItemIndeterminate(1), Is.True);
            Assert.That(menu.IsItemChecked(1), Is.False, "Godot clears checked when an item becomes indeterminate.");
            menu.ToggleItemChecked(1);
            Assert.That(menu.IsItemChecked(1), Is.True);
            Assert.That(menu.IsItemIndeterminate(1), Is.True, "Godot toggle_item_checked does not clear indeterminate.");
            menu.SetItemChecked(1, false);
            Assert.That(menu.IsItemIndeterminate(1), Is.False, "Godot set_item_checked clears indeterminate when checked changes.");

            menu.SetItemAsCheckable(0, true);
            Assert.That(menu.GetItem(0).Kind, Is.EqualTo(PopupMenuItemKind.Check));
            Assert.That(menu.IsItemCheckable(0), Is.True);
            menu.SetItemAsRadioCheckable(0, true);
            Assert.That(menu.GetItem(0).Kind, Is.EqualTo(PopupMenuItemKind.RadioCheck));
            Assert.That(menu.IsItemRadioCheckable(0), Is.True);
            menu.SetItemAsCheckable(0, false);
            Assert.That(menu.GetItem(0).Kind, Is.EqualTo(PopupMenuItemKind.Item));
            Assert.That(menu.IsItemCheckable(0), Is.False);

            menu.SetItemChecked(2, true);
            var pressedId = -1;
            menu.IdPressed += (_, id) => pressedId = id;
            var context = new UIContext();
            context.Add(menu);
            menu.PopupAt(Vector2.Zero, new Vector2(160, 0));

            context.Update(Time, Mouse(8, 80), new KeyboardState());
            context.Update(Time, Mouse(8, 80, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(8, 80), new KeyboardState());

            Assert.That(pressedId, Is.EqualTo(4));
            Assert.That(menu.IsItemChecked(2), Is.True, "Godot radio items do not automatically uncheck sibling items.");
            Assert.That(menu.IsItemChecked(3), Is.False, "Godot radio items do not automatically check the activated item.");
            Assert.That(menu.Visible, Is.True);
        }

        [Test]
        public void PopupMenu_MapsGodotItemPropertiesReorderAndRemoval()
        {
            var menu = new PopupMenu { HideOnItemSelection = false };
            menu.AddItem("Open", 10);
            menu.AddItem("Save", 20);
            menu.AddItem("Close", 30);
            var pressed = new List<int>();
            menu.IdPressed += (_, id) => pressed.Add(id);

            Assert.That(menu.GetItemCount(), Is.EqualTo(3));
            menu.SetItemText(1, "Save As");
            menu.SetItemId(1, 25);
            menu.SetItemMetadata(1, "res://save_as.tscn");
            menu.SetItemTooltip(1, "Save with a new name");
            menu.SetItemIndent(1, 2);
            menu.SetItemTextDirection(1, TextDirection.RightToLeft);
            menu.SetItemLanguage(1, "ar");
            menu.SetItemAutoTranslateMode(1, AutoTranslateMode.Disabled);
            menu.SetItemIcon(1, null);
            menu.SetItemIconMaxWidth(1, 12);
            menu.SetItemIconModulate(1, Color.CornflowerBlue);
            Assert.That(menu.GetItemText(1), Is.EqualTo("Save As"));
            Assert.That(menu.GetItemIdxFromText("Save As"), Is.EqualTo(1));
            Assert.That(menu.GetItemIdxFromText("Missing"), Is.EqualTo(-1));
            Assert.That(menu.GetItemId(1), Is.EqualTo(25));
            Assert.That(menu.GetItemIndex(25), Is.EqualTo(1));
            Assert.That(menu.GetItemIndex(20), Is.EqualTo(-1));
            Assert.That(menu.GetItemMetadata(1), Is.EqualTo("res://save_as.tscn"));
            Assert.That(menu.GetItemTooltip(1), Is.EqualTo("Save with a new name"));
            Assert.That(menu.GetItemIndent(1), Is.EqualTo(2));
            Assert.That(menu.GetItemTextDirection(1), Is.EqualTo(TextDirection.RightToLeft));
            Assert.That(menu.GetItemLanguage(1), Is.EqualTo("ar"));
            Assert.That(menu.GetItemAutoTranslateMode(1), Is.EqualTo(AutoTranslateMode.Disabled));
            Assert.That(menu.GetItemTextDirection(0), Is.EqualTo(TextDirection.Inherited));
            Assert.That(menu.GetItemLanguage(0), Is.Empty);
            Assert.That(menu.GetItemAutoTranslateMode(0), Is.EqualTo(AutoTranslateMode.Inherit));
            Assert.That(menu.GetItemIcon(1), Is.Null);
            Assert.That(menu.GetItemIconMaxWidth(1), Is.EqualTo(12));
            Assert.That(menu.GetItemIconModulate(1), Is.EqualTo(Color.CornflowerBlue));
            menu.SetItemLanguage(-2, null);
            menu.SetItemAutoTranslateMode(-2, AutoTranslateMode.Always);
            Assert.That(menu.GetItemLanguage(1), Is.Empty);
            Assert.That(menu.GetItemAutoTranslateMode(1), Is.EqualTo(AutoTranslateMode.Always));

            var context = new UIContext();
            context.Add(menu);
            menu.PopupAt(Vector2.Zero, new Vector2(160, 0));
            Assert.That(menu.ItemsControl.GetTooltip(new Point(8, 32)), Is.EqualTo("Save with a new name"));
            menu.SetItemDisabled(1, true);
            context.Update(Time, Mouse(8, 32), new KeyboardState());
            context.Update(Time, Mouse(8, 28, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(8, 28), new KeyboardState());
            Assert.That(pressed, Is.Empty);
            Assert.That(menu.IsItemDisabled(1), Is.True);

            menu.SetItemDisabled(1, false);
            menu.SetItemAsSeparator(1, true);
            Assert.That(menu.IsItemSeparator(1), Is.True);
            Assert.That(menu.GetItemText(1), Is.EqualTo("Save As"), "Godot separator conversion preserves retained item data.");
            context.Update(Time, Mouse(8, 28, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(8, 28), new KeyboardState());
            Assert.That(pressed, Is.Empty);
            menu.SetItemAsSeparator(1, false);
            Assert.That(menu.IsItemSeparator(1), Is.False);

            menu.SetItemIndex(2, 0);
            Assert.That(menu.GetItemText(0), Is.EqualTo("Close"));
            Assert.That(menu.GetItemIndex(30), Is.EqualTo(0));
            Assert.That(menu.GetItemIndex(25), Is.EqualTo(2));
            menu.RemoveItem(1);
            Assert.That(menu.GetItemCount(), Is.EqualTo(2));
            Assert.That(menu.GetItemText(1), Is.EqualTo("Save As"));
            Assert.That(menu.GetItemIndex(10), Is.EqualTo(-1));
        }

        [Test]
        public void PopupMenu_MapsGodotAddIconAndSeparatorOverloads()
        {
            var menu = new PopupMenu { HideOnItemSelection = false, HideOnCheckableItemSelection = false };
            var openAccelerator = new PopupMenuShortcut("Open", Keys.O, control: true);
            var snapAccelerator = new PopupMenuShortcut("Snap", Keys.S, control: true);
            var moveAccelerator = new PopupMenuShortcut("Move", Keys.M, control: true);
            var duplicateShortcut = new PopupMenuShortcut("Duplicate", Keys.D, control: true);
            var gridShortcut = new PopupMenuShortcut("Grid", Keys.G, control: true);
            var rotateShortcut = new PopupMenuShortcut("Rotate", Keys.R, control: true);

            var open = menu.AddIconItem(null, "Open", 10, openAccelerator);
            var snap = menu.AddIconCheckItem(null, "Snap", 20, snapAccelerator);
            var move = menu.AddIconRadioCheckItem(null, "Move", 30, moveAccelerator);
            var duplicate = menu.AddIconShortcut(null, duplicateShortcut, 40, global: true, allowEcho: true);
            var grid = menu.AddIconCheckShortcut(null, gridShortcut, 50);
            var rotate = menu.AddIconRadioCheckShortcut(null, rotateShortcut, 60);
            var separator = menu.AddSeparator("Tools", 70);
            var pressed = new List<int>();
            menu.IdPressed += (_, id) => pressed.Add(id);

            Assert.That(menu.GetItemCount(), Is.EqualTo(7));
            Assert.That(open.Accelerator, Is.SameAs(openAccelerator));
            Assert.That(snap.Accelerator, Is.SameAs(snapAccelerator));
            Assert.That(move.Accelerator, Is.SameAs(moveAccelerator));
            Assert.That(duplicate.Shortcut, Is.SameAs(duplicateShortcut));
            Assert.That(duplicate.ShortcutIsGlobal, Is.True);
            Assert.That(grid.CheckableType, Is.EqualTo(PopupMenuCheckableType.Check));
            Assert.That(rotate.CheckableType, Is.EqualTo(PopupMenuCheckableType.Radio));
            Assert.That(separator.Separator, Is.True);
            Assert.That(menu.GetItemText(6), Is.EqualTo("Tools"));
            Assert.That(menu.GetItemId(6), Is.EqualTo(70));
            Assert.That(menu.GetItemIcon(0), Is.Null);
            Assert.That(menu.GetItemIcon(3), Is.Null);

            Assert.That(menu.ActivateItemByShortcut(Keys.O, new KeyboardState(Keys.LeftControl, Keys.O)), Is.True);
            Assert.That(menu.ActivateItemByShortcut(Keys.D, new KeyboardState(Keys.LeftControl, Keys.D), globalOnly: true), Is.True);
            Assert.That(menu.ActivateItemByShortcut(Keys.G, new KeyboardState(Keys.LeftControl, Keys.G), globalOnly: true), Is.False);
            Assert.That(menu.ActivateItemByShortcut(Keys.G, new KeyboardState(Keys.LeftControl, Keys.G)), Is.True);
            Assert.That(pressed, Is.EqualTo(new[] { 10, 40, 50 }));

            var context = new UIContext();
            context.Add(menu);
            menu.PopupAt(Vector2.Zero, new Vector2(160, 0));
            context.Update(Time, Mouse(8, 148, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(8, 148), new KeyboardState());

            Assert.That(pressed, Is.EqualTo(new[] { 10, 40, 50 }), "Godot separators retain text and IDs but are not activatable items.");
        }

        [Test]
        public void PopupMenu_MapsGodotMultistateItemsAndHidePolicy()
        {
            var menu = new PopupMenu();
            var multistate = menu.AddMultistateItem("Layout mode", 3, 1, 7, new PopupMenuShortcut("Layout mode", Keys.L, control: true));
            var pressed = new List<int>();
            var focused = -1;
            menu.IdPressed += (_, id) => pressed.Add(id);
            menu.IndexPressed += (_, index) => focused = index;
            var context = new UIContext();
            context.Add(menu);
            menu.PopupAt(Vector2.Zero, new Vector2(160, 0));

            Assert.That(menu.IsHideOnStateItemSelection(), Is.False, "Godot keeps multistate popup selections open by default.");
            Assert.That(menu.IsHideOnItemSelection(), Is.True);
            menu.SetHideOnItemSelection(false);
            Assert.That(menu.IsHideOnItemSelection(), Is.False);
            menu.SetHideOnItemSelection(true);
            Assert.That(multistate.Kind, Is.EqualTo(PopupMenuItemKind.MultiState));
            Assert.That(menu.GetItemMultistateMax(0), Is.EqualTo(3));
            Assert.That(menu.GetItemMultistate(0), Is.EqualTo(1));

            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(8, 8), new KeyboardState());

            Assert.That(pressed, Is.EqualTo(new[] { 7 }));
            Assert.That(focused, Is.EqualTo(0));
            Assert.That(menu.Visible, Is.True);
            Assert.That(menu.GetItemState(0), Is.EqualTo(1), "Godot activation emits signals but does not toggle state implicitly.");

            menu.ToggleItemMultistate(0);
            Assert.That(menu.GetItemMultistate(0), Is.EqualTo(2));
            menu.ToggleItemMultistate(0);
            Assert.That(menu.GetItemMultistate(0), Is.EqualTo(0));
            menu.SetItemMultistateMax(0, 4);
            menu.SetItemMultistate(0, 3);
            Assert.That(menu.GetItemMaxStates(0), Is.EqualTo(4));
            Assert.That(menu.GetItemState(0), Is.EqualTo(3));

            menu.SetHideOnStateItemSelection(true);
            Assert.That(menu.IsHideOnMultistateItemSelection(), Is.True);
            Assert.That(menu.ActivateItemByShortcut(Keys.L, new KeyboardState(Keys.LeftControl, Keys.L)), Is.True);
            Assert.That(pressed, Is.EqualTo(new[] { 7, 7 }));
            Assert.That(menu.Visible, Is.False);
        }

        [Test]
        public void PopupMenu_MapsGodotFocusedItemAndScrollApis()
        {
            var menu = new PopupMenu();
            menu.AddItem("Open");
            menu.AddSeparator();
            menu.AddItem("Save");
            var focused = new List<int>();
            menu.IndexFocused += (_, index) => focused.Add(index);

            Assert.That(menu.GetFocusedItem(), Is.EqualTo(-1));

            menu.SetFocusedItem(2);

            Assert.That(menu.HighlightedIndex, Is.EqualTo(2));
            Assert.That(menu.GetFocusedItem(), Is.EqualTo(2));
            Assert.That(focused, Is.EqualTo(new[] { 2 }));

            menu.SetFocusedItem(1);

            Assert.That(menu.GetFocusedItem(), Is.EqualTo(1), "Godot permits programmatic focus on any valid item index, including separators.");
            Assert.That(focused, Is.EqualTo(new[] { 2, 1 }));

            menu.SetFocusedItem(-1);

            Assert.That(menu.GetFocusedItem(), Is.EqualTo(-1));
            Assert.That(focused, Is.EqualTo(new[] { 2, 1 }), "Clearing focus does not emit an item-focused signal.");

            menu.ScrollToItem(0);

            Assert.That(menu.GetFocusedItem(), Is.EqualTo(-1), "Godot scroll_to_item adjusts visibility but does not select or focus an item.");
            Assert.Throws<ArgumentOutOfRangeException>(() => menu.SetFocusedItem(-2));
            Assert.Throws<ArgumentOutOfRangeException>(() => menu.SetFocusedItem(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => menu.ScrollToItem(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => menu.ScrollToItem(3));
        }

        [Test]
        public void PopupMenu_MapsGodotShrinkWidthAndHeightSizing()
        {
            var menu = new PopupMenu();
            menu.AddItem("Open");
            menu.AddItem("Save");
            menu.Size = new Vector2(320, 180);

            Assert.That(menu.GetShrinkWidth(), Is.True);
            Assert.That(menu.GetShrinkHeight(), Is.True);
            menu.PopupAt(Vector2.Zero, new Vector2(120, 0));
            Assert.That(menu.Size, Is.EqualTo(new Vector2(140, 50)), "Godot shrink flags default to true and recalculate popup size from minimum content.");

            menu.Size = new Vector2(320, 180);
            menu.SetShrinkWidth(false);
            menu.SetShrinkHeight(false);
            menu.PopupAt(Vector2.Zero, new Vector2(120, 0));
            Assert.That(menu.Size, Is.EqualTo(new Vector2(320, 180)));

            menu.SetShrinkWidth(true);
            menu.PopupAt(Vector2.Zero, new Vector2(200, 0));
            Assert.That(menu.Size, Is.EqualTo(new Vector2(200, 180)), "Only the shrink-enabled axis is recalculated.");

            menu.Size = new Vector2(320, 180);
            menu.SetShrinkWidth(false);
            menu.SetShrinkHeight(true);
            menu.PopupAt(Vector2.Zero, new Vector2(120, 0));
            Assert.That(menu.Size, Is.EqualTo(new Vector2(320, 50)));
        }

        [Test]
        public void PopupMenu_ExpandsDefaultRowsToFitDynamicFont()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var menu = new PopupMenu { UIFont = new DynamicUIFont(face, 24) };
            menu.AddItem("First");
            menu.AddItem("Second");
            var context = new UIContext();
            context.Add(menu);

            menu.PopupAt(Vector2.Zero, new Vector2(120, 0));

            Assert.That(menu.EffectiveItemHeight, Is.GreaterThan(menu.ItemHeight));
            Assert.That(menu.Size.Y, Is.EqualTo(menu.EffectiveItemHeight * 2 + 2));
            Assert.That(menu.ItemAt(new Point(8, (int)menu.EffectiveItemHeight + 2)), Is.EqualTo(1));
        }

        [Test]
        public void PopupMenu_AlignsStateIconWithVisibleItemGlyphs()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 24);
            var layout = TextMetrics.Layout(font, "100%");
            var row = new Rectangle(0, 10, 200, 36);
            var textY = row.Y + Math.Max(2, (row.Height - TextMetrics.LineHeight(font)) / 2);
            var iconY = PopupMenu.GetItemStateIconY(layout, textY, row, 16);
            var visibleTop = layout.VisibleGlyphs.Min(glyph => glyph.Bounds.Top) + textY;
            var visibleBottom = layout.VisibleGlyphs.Max(glyph => glyph.Bounds.Bottom) + textY;

            Assert.That(iconY + 8, Is.EqualTo((visibleTop + visibleBottom) / 2).Within(.001f));

            var emptyLayout = TextMetrics.Layout(font, string.Empty);
            Assert.That(
                PopupMenu.GetItemStateIconY(emptyLayout, textY, row, 16),
                Is.EqualTo(row.Center.Y - 8));
        }

        [Test]
        public void PopupMenu_MapsGodotSearchConfigurationAndIncrementalFocus()
        {
            var menu = new PopupMenu();
            menu.AddItem("Open", 1);
            menu.AddItem("Camera", 2);
            menu.AddItem("Canvas", 3);
            var focused = new List<int>();
            menu.IndexFocused += (_, index) => focused.Add(index);
            var context = new UIContext();
            context.Add(menu);
            menu.PopupAt(Vector2.Zero, new Vector2(160, 0));

            Assert.That(menu.GetAllowSearch(), Is.True);
            Assert.That(menu.IsSearchBarEnabled(), Is.False);
            Assert.That(menu.GetSearchBarMinItemCount(), Is.Zero);
            Assert.That(menu.IsSearchBarFuzzySearchEnabled(), Is.True);
            Assert.That(menu.GetSearchBarFuzzySearchMaxMisses(), Is.EqualTo(2));
            Assert.That(menu.IsPreferNativeMenu(), Is.False);
            Assert.That(menu.IsNativeMenu(), Is.False);
            Assert.That(menu.IsSystemMenu(), Is.False);
            Assert.That(menu.GetSystemMenu(), Is.EqualTo(PopupSystemMenu.Invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => menu.SetSearchBarMinItemCount(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => menu.SetSearchBarFuzzySearchMaxMisses(-1));

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(300, 200), new KeyboardState());
            context.TextInput('c');
            Assert.That(menu.GetFocusedItem(), Is.EqualTo(1));
            context.Update(new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100)), Mouse(300, 200), new KeyboardState());
            context.TextInput('a');
            Assert.That(menu.GetFocusedItem(), Is.EqualTo(2), "Incremental search appends within the timeout and searches from the next item.");
            context.Update(new GameTime(TimeSpan.FromMilliseconds(1200), TimeSpan.FromMilliseconds(1100)), Mouse(300, 200), new KeyboardState());
            context.TextInput('o');
            Assert.That(menu.GetFocusedItem(), Is.EqualTo(0), "Search text resets after the incremental-search timeout.");

            menu.SetAllowSearch(false);
            context.Update(new GameTime(TimeSpan.FromMilliseconds(1300), TimeSpan.FromMilliseconds(100)), Mouse(300, 200), new KeyboardState());
            context.TextInput('c');
            Assert.That(menu.GetFocusedItem(), Is.EqualTo(0));

            menu.SetAllowSearch(true);
            menu.SetSearchBarEnabled(true);
            menu.SetSearchBarMinItemCount(2);
            menu.SetSearchBarFuzzySearchEnabled(false);
            menu.SetSearchBarFuzzySearchMaxMisses(4);

            Assert.That(menu.IsSearchBarVisible, Is.True);
            Assert.That(menu.IsSearchBarEnabled(), Is.True);
            Assert.That(menu.GetSearchBarMinItemCount(), Is.EqualTo(2));
            Assert.That(menu.IsSearchBarFuzzySearchEnabled(), Is.False);
            Assert.That(menu.GetSearchBarFuzzySearchMaxMisses(), Is.EqualTo(4));
            menu.SetPreferNativeMenu(true);
            menu.SetSystemMenu(PopupSystemMenu.Application);
            Assert.That(menu.IsPreferNativeMenu(), Is.True);
            Assert.That(menu.GetSystemMenu(), Is.EqualTo(PopupSystemMenu.Application));
            Assert.That(menu.IsNativeMenu(), Is.False, "The retained MonoGame backend stores native-menu preference but does not claim a platform native menu binding.");
            Assert.That(menu.IsSystemMenu(), Is.False);
            context.Update(new GameTime(TimeSpan.FromMilliseconds(1400), TimeSpan.FromMilliseconds(100)), Mouse(300, 200), new KeyboardState());
            context.TextInput('c');
            Assert.That(menu.GetSearchBarText(), Is.EqualTo("c"));
            Assert.That(menu.GetItem(0).Visible, Is.False);
            Assert.That(menu.GetItem(1).Visible, Is.True);
            Assert.That(menu.GetItem(2).Visible, Is.True);
            Assert.That(menu.GetFocusedItem(), Is.EqualTo(1), "Typed text is routed to the visible search bar and filtering moves focus only because the previous item is now hidden.");

            Assert.That(focused, Does.Contain(1));
            Assert.That(focused, Does.Contain(2));
        }

        [Test]
        public void PopupMenu_MapsGodotVisibleSearchBarFiltering()
        {
            var menu = new PopupMenu();
            var submenu = new PopupMenu();
            submenu.SetSearchBarFuzzySearchEnabled(false);
            submenu.AddItem("Profiler", 40, new PopupMenuShortcut("Profiler", Keys.P, control: true));
            submenu.AddSeparator();
            submenu.AddItem("Inspector", 41);
            menu.AddItem("Open", 1, new PopupMenuShortcut("Open", Keys.O, control: true));
            menu.AddSeparator("Recent");
            menu.AddItem("Camera", 2);
            menu.AddItem("Canvas", 3);
            menu.AddSubmenuItem("Tools", submenu, 4);
            var pressed = new List<int>();
            menu.IdPressed += (_, id) => pressed.Add(id);
            submenu.IdPressed += (_, id) => pressed.Add(id);

            var context = new UIContext();
            context.Add(menu);
            menu.SetSearchBarEnabled(true);
            menu.SetSearchBarFuzzySearchEnabled(false);
            menu.PopupAt(Vector2.Zero, new Vector2(160, 0));

            Assert.That(menu.IsSearchBarVisible, Is.True);
            Assert.That(menu.GetItem(0).Visible, Is.True);
            Assert.That(menu.GetItem(1).Visible, Is.True);
            Assert.That(menu.GetItem(2).Visible, Is.True);
            Assert.That(menu.GetItem(3).Visible, Is.True);
            Assert.That(menu.GetItem(4).Visible, Is.True);

            menu.SetSearchBarText("can");
            menu.PopupAt(Vector2.Zero, new Vector2(160, 0), false);
            Assert.That(menu.GetItem(0).Visible, Is.False);
            Assert.That(menu.GetItem(1).Visible, Is.False, "Separators do not remain visible while a search query is active.");
            Assert.That(menu.GetItem(2).Visible, Is.False);
            Assert.That(menu.GetItem(3).Visible, Is.True);
            Assert.That(menu.GetItem(4).Visible, Is.False);
            Assert.That(menu.Size.Y, Is.EqualTo(54));
            Assert.That(menu.GetSearchBarBounds(), Is.EqualTo(new Rectangle(4, 4, 152, 20)));
            Assert.That(menu.ItemAt(new Point(4, 4)), Is.EqualTo(-1), "The visible search bar owns the top lane above the item list.");
            Assert.That(menu.ItemAt(new Point(4, 32)), Is.EqualTo(3), "Hit testing returns the original item index of the first visible row below the search bar.");
            Assert.That(menu.ActivateItemByShortcut(Keys.O, new KeyboardState(Keys.LeftControl, Keys.O)), Is.False, "Hidden filtered items cannot be shortcut-activated.");

            menu.KeyPressed(Keys.Back);
            Assert.That(menu.GetSearchBarText(), Is.EqualTo("ca"));
            menu.KeyPressed(Keys.Delete);
            Assert.That(menu.GetSearchBarText(), Is.Empty);
            Assert.That(menu.Visible, Is.True);
            menu.SetSearchBarText("prof");
            menu.KeyPressed(Keys.Escape);
            Assert.That(menu.GetSearchBarText(), Is.Empty, "Escape clears an active visible search query before closing the menu.");
            Assert.That(menu.Visible, Is.True);
            menu.KeyPressed(Keys.Escape);
            Assert.That(menu.Visible, Is.False);
            menu.PopupAt(Vector2.Zero, new Vector2(160, 0), false);

            menu.SetSearchBarText("prof");
            Assert.That(menu.GetItem(4).Visible, Is.True, "A submenu parent remains visible when a child matches.");
            Assert.That(submenu.GetItem(0).Visible, Is.True);
            Assert.That(submenu.GetItem(1).Visible, Is.False);
            Assert.That(submenu.GetItem(2).Visible, Is.False);

            menu.SetSearchBarText("tools");
            Assert.That(menu.GetItem(4).Visible, Is.True);
            Assert.That(submenu.GetItem(0).Visible, Is.True, "Matching a submenu parent reveals all child rows, matching Godot's filter propagation.");
            Assert.That(submenu.GetItem(1).Visible, Is.True);
            Assert.That(submenu.GetItem(2).Visible, Is.True);

            menu.SetSearchBarFuzzySearchEnabled(true);
            menu.SetSearchBarFuzzySearchMaxMisses(0);
            menu.SetSearchBarText("cnv");
            Assert.That(menu.GetItem(3).Visible, Is.True, "Fuzzy search accepts ordered subsequence matches.");
            menu.SetSearchBarFuzzySearchMaxMisses(2);
            menu.SetSearchBarText("cxz");
            Assert.That(menu.GetItem(3).Visible, Is.True, "Fuzzy search allows configured missed query characters.");
            menu.SetSearchBarFuzzySearchEnabled(false);
            Assert.That(menu.GetItem(3).Visible, Is.False, "Exact-token mode rejects the same non-contiguous fuzzy query.");

            menu.SetSearchBarText(string.Empty);
            Assert.That(menu.GetItem(0).Visible, Is.True);
            Assert.That(menu.GetItem(1).Visible, Is.True);
            Assert.That(menu.GetItem(2).Visible, Is.True);
            Assert.That(menu.GetItem(3).Visible, Is.True);
            Assert.That(menu.GetItem(4).Visible, Is.True);
        }

        [Test]
        public void PopupMenu_SearchBarVisibilityUsesNonSeparatorItemCount()
        {
            var menu = new PopupMenu();
            menu.SetSearchBarEnabled(true);
            menu.SetSearchBarMinItemCount(3);
            menu.AddItem("Open");
            menu.AddSeparator("Recent");
            menu.AddItem("Save");

            Assert.That(menu.GetItemCount(), Is.EqualTo(3));
            Assert.That(menu.IsSearchBarVisible, Is.False, "Godot counts non-separator items for search-bar visibility.");
            Assert.That(menu.GetSearchBarBounds(), Is.EqualTo(Rectangle.Empty));

            menu.AddItem("Close");
            menu.PopupAt(Vector2.Zero, new Vector2(160, 0));

            Assert.That(menu.IsSearchBarVisible, Is.True);
            Assert.That(menu.GetSearchBarBounds(), Is.EqualTo(new Rectangle(4, 4, 152, 20)));
            Assert.That(menu.Size.Y, Is.EqualTo(109));
        }

        [Test]
        public void PopupMenu_SearchBarPointerFocusAndClearButton()
        {
            var menu = new PopupMenu();
            menu.AddItem("Open", 1);
            menu.AddItem("Camera", 2);
            menu.AddItem("Canvas", 3);
            var context = new UIContext();
            context.Add(menu);
            menu.SetSearchBarEnabled(true);
            menu.SetSearchBarFuzzySearchEnabled(false);
            menu.PopupAt(Vector2.Zero, new Vector2(160, 0));

            Assert.That(menu.IsSearchBarFocused, Is.False);
            Assert.That(menu.GetSearchBarClearButtonBounds(), Is.EqualTo(Rectangle.Empty));

            menu.HandleItemsPressed(new Point(12, 10));
            Assert.That(menu.IsSearchBarFocused, Is.True);
            Assert.That(menu.GetFocusedItem(), Is.EqualTo(-1));

            context.TextInput('c');
            context.TextInput('a');
            Assert.That(menu.GetSearchBarText(), Is.EqualTo("ca"));
            Assert.That(menu.GetSearchBarCaretColumn(), Is.EqualTo(2));
            Assert.That(menu.GetSearchBarClearButtonBounds(), Is.EqualTo(new Rectangle(132, 4, 20, 20)));
            Assert.That(menu.GetItem(0).Visible, Is.False);
            Assert.That(menu.GetItem(1).Visible, Is.True);
            Assert.That(menu.GetItem(2).Visible, Is.True);

            menu.SetSearchBarText("can");
            Assert.That(menu.GetSearchBarCaretColumn(), Is.EqualTo(3));
            menu.SetSearchBarCaretColumn(1);
            context.TextInput('o');
            Assert.That(menu.GetSearchBarText(), Is.EqualTo("coan"));
            Assert.That(menu.GetSearchBarCaretColumn(), Is.EqualTo(2));
            menu.KeyPressed(Keys.Right);
            Assert.That(menu.GetSearchBarCaretColumn(), Is.EqualTo(3));
            menu.KeyPressed(Keys.Back);
            Assert.That(menu.GetSearchBarText(), Is.EqualTo("con"));
            Assert.That(menu.GetSearchBarCaretColumn(), Is.EqualTo(2));
            menu.KeyPressed(Keys.Home);
            Assert.That(menu.GetSearchBarCaretColumn(), Is.Zero);
            menu.KeyPressed(Keys.Delete);
            Assert.That(menu.GetSearchBarText(), Is.EqualTo("on"));
            Assert.That(menu.GetSearchBarCaretColumn(), Is.Zero);
            menu.KeyPressed(Keys.End);
            context.TextInput('e');
            Assert.That(menu.GetSearchBarText(), Is.EqualTo("one"));
            Assert.That(menu.GetSearchBarCaretColumn(), Is.EqualTo(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => menu.SetSearchBarCaretColumn(4));

            menu.HandleItemsPressed(new Point(148, 10));
            Assert.That(menu.IsSearchBarFocused, Is.True);
            Assert.That(menu.GetSearchBarText(), Is.Empty);
            Assert.That(menu.GetSearchBarClearButtonBounds(), Is.EqualTo(Rectangle.Empty));
            Assert.That(menu.GetItem(0).Visible, Is.True);
            Assert.That(menu.GetItem(1).Visible, Is.True);
            Assert.That(menu.GetItem(2).Visible, Is.True);

            menu.HandleItemsPressed(new Point(8, 32));
            Assert.That(menu.IsSearchBarFocused, Is.False);
            Assert.That(menu.GetFocusedItem(), Is.EqualTo(0));
        }

        [Test]
        public void PopupMenu_MapsGodotNestedSubmenuHoverAndKeyboardLifecycle()
        {
            var menu = new PopupMenu { Size = new Vector2(120, 0), SubmenuPopupDelay = TimeSpan.FromMilliseconds(200) };
            var export = new PopupMenu { SubmenuPopupDelay = TimeSpan.FromMilliseconds(200) };
            var import = new PopupMenu { SubmenuPopupDelay = TimeSpan.FromMilliseconds(200) };
            export.AddItem("PNG", 10);
            import.AddItem("Scene", 20);
            menu.AddSubmenuItem("Export", export);
            menu.AddSubmenuItem("Import", import);
            menu.AddItem("Quit", 30);
            var pressedId = -1;
            export.IdPressed += (_, id) => pressedId = id;
            var context = new UIContext { ViewportSize = new Vector2(400, 240) };
            context.Add(menu);
            menu.PopupAt(Vector2.Zero, new Vector2(120, 0));

            menu.SetSubmenuPopupDelay(0);
            Assert.That(menu.GetSubmenuPopupDelay(), Is.EqualTo(0.01f).Within(0.0001f), "Godot clamps non-positive submenu delays to 0.01 seconds.");
            menu.SetSubmenuPopupDelay(0.2f);
            Assert.That(menu.GetSubmenuPopupDelay(), Is.EqualTo(0.2f).Within(0.0001f));

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(8, 8), new KeyboardState());
            Assert.That(export.Visible, Is.False, "Godot delays pointer-opened submenus.");
            context.Update(new GameTime(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250)), Mouse(8, 8), new KeyboardState());
            Assert.That(export.Visible, Is.True);
            Assert.That(menu.ActiveSubmenuIndex, Is.EqualTo(0));
            Assert.That(export.GlobalPosition, Is.EqualTo(new Vector2(menu.Bounds.Right - 1, 1)));

            context.Update(new GameTime(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50)), Mouse(8, 32), new KeyboardState());
            Assert.That(export.Visible, Is.False);
            Assert.That(import.Visible, Is.False);
            context.Update(new GameTime(TimeSpan.FromMilliseconds(550), TimeSpan.FromMilliseconds(250)), Mouse(8, 32), new KeyboardState());
            Assert.That(import.Visible, Is.True, "Hovering a sibling submenu switches the active branch after the submenu delay.");
            Assert.That(menu.ActiveSubmenuIndex, Is.EqualTo(1));

            context.Update(new GameTime(TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(50)), Mouse(8, 56), new KeyboardState());
            Assert.That(import.Visible, Is.False, "Hovering a non-submenu item closes the active submenu branch.");

            context.Update(new GameTime(TimeSpan.FromMilliseconds(650), TimeSpan.FromMilliseconds(50)), Mouse(8, 8), new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromMilliseconds(700), TimeSpan.FromMilliseconds(50)), Mouse(8, 8), new KeyboardState(Keys.Right));
            Assert.That(export.Visible, Is.True, "Right opens the focused submenu immediately.");
            Assert.That(context.FocusedControl, Is.SameAs(export));
            Assert.That(export.HighlightedIndex, Is.EqualTo(0));

            context.Update(new GameTime(TimeSpan.FromMilliseconds(750), TimeSpan.FromMilliseconds(50)), Mouse(8, 8), new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(50)), Mouse(8, 8), new KeyboardState(Keys.Left));
            Assert.That(export.Visible, Is.False);
            Assert.That(menu.Visible, Is.True);
            Assert.That(context.FocusedControl, Is.SameAs(menu));

            context.Update(new GameTime(TimeSpan.FromMilliseconds(850), TimeSpan.FromMilliseconds(50)), Mouse(8, 8), new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(50)), Mouse(8, 8), new KeyboardState(Keys.Right));
            context.Update(new GameTime(TimeSpan.FromMilliseconds(950), TimeSpan.FromMilliseconds(50)), Mouse(8, 8), new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(50)), Mouse(8, 8), new KeyboardState(Keys.Enter));

            Assert.That(pressedId, Is.EqualTo(10));
            Assert.That(menu.Visible, Is.False);
            Assert.That(export.Visible, Is.False);
        }

        [Test]
        public void PopupMenu_MapsGodotSubmenuNodeItemMutationApis()
        {
            var menu = new PopupMenu { Size = new Vector2(140, 0) };
            var export = new PopupMenu();
            var import = new PopupMenu();
            export.AddItem("PNG", 10);
            import.AddItem("Scene", 20);
            menu.AddItem("Open", 1);
            menu.AddItem("Export", 2);

            menu.SetItemSubmenuNode(-1, export);

            Assert.That(menu.GetItemSubmenuNode(1), Is.SameAs(export));
            Assert.That(menu.GetItemSubmenu(1), Is.SameAs(export));
            Assert.That(menu.GetItem(1).Kind, Is.EqualTo(PopupMenuItemKind.Submenu));
            Assert.That(export.Parent, Is.SameAs(menu));

            var added = menu.AddSubmenuNodeItem("Import", import, 3);

            Assert.That(added.Submenu, Is.SameAs(import));
            Assert.That(menu.GetItemSubmenuNode(2), Is.SameAs(import));
            Assert.That(import.Parent, Is.SameAs(menu));

            var pressed = new List<int>();
            menu.IdPressed += (_, id) => pressed.Add(id);
            export.IdPressed += (_, id) => pressed.Add(id);
            var context = new UIContext { ViewportSize = new Vector2(400, 240) };
            context.Add(menu);
            menu.PopupAt(Vector2.Zero, new Vector2(140, 0));
            menu.SetFocusedItem(1);

            context.Update(Time, Mouse(300, 200), new KeyboardState(Keys.Right));
            context.Update(Time, Mouse(300, 200), new KeyboardState());

            Assert.That(export.Visible, Is.True);
            Assert.That(context.FocusedControl, Is.SameAs(export));
            Assert.That(export.HighlightedIndex, Is.EqualTo(0));
            Assert.That(pressed, Is.Empty, "Opening a submenu does not activate the parent item.");

            context.Update(Time, Mouse(300, 200), new KeyboardState(Keys.Enter));
            context.Update(Time, Mouse(300, 200), new KeyboardState());

            Assert.That(pressed, Is.EqualTo(new[] { 10 }));
            Assert.Throws<ArgumentNullException>(() => menu.SetItemSubmenuNode(0, null));

            var otherParent = new PopupMenu();
            var alreadyParented = new PopupMenu();
            otherParent.AddChild(alreadyParented);

            Assert.Throws<InvalidOperationException>(() => menu.SetItemSubmenuNode(0, alreadyParented));
        }

        [Test]
        public void PopupMenu_MapsGodotSubmenuPathStateApis()
        {
            var menu = new PopupMenu { Size = new Vector2(140, 0) };
            var added = menu.AddSubmenuItem("Export", "ExportMenu", 4);

            Assert.That(added.Kind, Is.EqualTo(PopupMenuItemKind.Submenu));
            Assert.That(added.SubmenuPath, Is.EqualTo("ExportMenu"));
            Assert.That(menu.GetItemSubmenuPath(0), Is.EqualTo("ExportMenu"));
            Assert.That(menu.GetItemSubmenuNode(0), Is.Null);

            menu.SetItemSubmenu(0, "ToolsMenu");

            Assert.That(menu.GetItemSubmenuPath(0), Is.EqualTo("ToolsMenu"));
            Assert.That(menu.GetItem(0).Kind, Is.EqualTo(PopupMenuItemKind.Submenu));

            var pressed = new List<int>();
            menu.IdPressed += (_, id) => pressed.Add(id);
            var context = new UIContext { ViewportSize = new Vector2(400, 240) };
            context.Add(menu);
            menu.PopupAt(Vector2.Zero, new Vector2(140, 0));
            menu.SetFocusedItem(0);

            context.Update(Time, Mouse(300, 200), new KeyboardState(Keys.Enter));
            context.Update(Time, Mouse(300, 200), new KeyboardState());

            Assert.That(pressed, Is.Empty, "A path-only submenu item remains a branch item, not a command activation.");
            Assert.That(menu.Visible, Is.True);

            menu.SetItemSubmenuPath(0, null);

            Assert.That(menu.GetItemSubmenuPath(0), Is.Empty);
            Assert.That(menu.GetItem(0).Kind, Is.EqualTo(PopupMenuItemKind.Item));

            var nodeSubmenu = new PopupMenu();
            menu.SetItemSubmenuNode(0, nodeSubmenu);
            menu.SetItemSubmenuPath(0, "CompatibilityName");

            Assert.That(menu.GetItemSubmenuNode(0), Is.SameAs(nodeSubmenu));
            Assert.That(menu.GetItemSubmenuPath(0), Is.EqualTo("CompatibilityName"));
            Assert.That(menu.GetItem(0).Kind, Is.EqualTo(PopupMenuItemKind.Submenu));
        }

        [Test]
        public void PopupMenu_MapsGodotAcceleratorAndShortcutActivationRouting()
        {
            var menu = new PopupMenu { HideOnCheckableItemSelection = false };
            menu.AddItem("Open", 1);
            menu.SetItemAccelerator(0, new PopupMenuShortcut("Open", Keys.O, control: true));
            var check = menu.AddCheckShortcut(new PopupMenuShortcut("Snap", Keys.S, control: true), 2);
            var disabled = menu.AddShortcut(new PopupMenuShortcut("Disabled", Keys.D, control: true), 3);
            disabled.ShortcutDisabled = true;
            var export = new PopupMenu();
            export.AddShortcut(new PopupMenuShortcut("Packed scene", Keys.P, control: true), 4);
            export.AddShortcut(new PopupMenuShortcut("Global export", Keys.G, control: true), 5, global: true);
            menu.AddSubmenuItem("Export", export, 6);
            var pressed = new List<int>();
            menu.IdPressed += (_, id) => pressed.Add(id);
            export.IdPressed += (_, id) => pressed.Add(id);
            var context = new UIContext(); context.Add(menu); menu.PopupAt(Vector2.Zero, new Vector2(160, 0));

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.O));
            Assert.That(pressed, Is.EqualTo(new[] { 1 }));
            Assert.That(menu.Visible, Is.False, "Accelerators activate items and use the normal hide-on-selection policy.");

            menu.PopupAt(Vector2.Zero, new Vector2(160, 0));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.S));
            Assert.That(check.Checked, Is.False, "Shortcut activation follows Godot activate_item and leaves checked state application-controlled.");
            Assert.That(menu.Visible, Is.True, "Check shortcuts honor HideOnCheckableItemSelection.");
            Assert.That(pressed, Is.EqualTo(new[] { 1, 2 }));

            Assert.That(menu.ActivateItemByShortcut(Keys.D, new KeyboardState(Keys.LeftControl, Keys.D)), Is.False);
            Assert.That(menu.ActivateItemByShortcut(Keys.P, new KeyboardState(Keys.LeftControl, Keys.P), globalOnly: true), Is.False);
            Assert.That(menu.ActivateItemByShortcut(Keys.G, new KeyboardState(Keys.LeftControl, Keys.G), globalOnly: true), Is.True);
            Assert.That(pressed, Is.EqualTo(new[] { 1, 2, 5 }));
            Assert.That(menu.GetItemAccelerator(0).DisplayText, Is.EqualTo("Ctrl+O"));
            Assert.That(menu.IsItemShortcutDisabled(2), Is.True);
            Assert.That(export.IsItemShortcutGlobal(1), Is.True);
        }

        [Test]
        public void MenuButton_SynchronizesPopupStateHoverSwitchingAndRtlPlacement()
        {
            var context = new UIContext();
            var bar = new MenuBar { Size = new Vector2(160, 28) };
            var file = bar.AddMenu("File"); file.SwitchOnHover = true;
            var edit = bar.AddMenu("Edit"); edit.SwitchOnHover = true;
            file.Menu.AddItem("Open"); edit.Menu.AddItem("Undo");
            file.ItemCount = 2;
            var aboutToPopup = 0; var shown = 0; var hidden = 0;
            file.AboutToPopup += (_, _) => aboutToPopup++;
            file.PopupShown += (_, _) => shown++;
            file.PopupHidden += (_, _) => hidden++;
            context.Add(bar);

            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(60, 8), new KeyboardState());

            Assert.That(file.ItemCount, Is.EqualTo(2));
            Assert.That(aboutToPopup, Is.EqualTo(1));
            Assert.That(shown, Is.EqualTo(1));
            Assert.That(hidden, Is.EqualTo(1));
            Assert.That(file.Menu.Visible, Is.False);
            Assert.That(file.ButtonPressed, Is.False);
            Assert.That(edit.Menu.Visible, Is.True);
            Assert.That(edit.ButtonPressed, Is.True);

            var rtl = new MenuButton { Position = new Vector2(20, 10), Size = new Vector2(80, 24), LayoutDirection = LayoutDirection.RightToLeft };
            rtl.Menu.CustomMinimumSize = new Vector2(120, 0);
            var rtlContext = new UIContext(); rtlContext.Add(rtl); rtl.ShowPopup();

            Assert.That(rtl.Menu.Position, Is.EqualTo(new Vector2(-20, 34)));

            var keyboard = new MenuButton { Size = new Vector2(80, 24) };
            var keyboardContext = new UIContext(); keyboardContext.Add(keyboard); keyboard.GrabFocus();
            keyboardContext.Update(Time, Mouse(0, 0), new KeyboardState());
            keyboardContext.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Enter));

            Assert.That(keyboard.Menu.Visible, Is.True);
            Assert.That(keyboard.ButtonPressed, Is.True);
        }

        [Test]
        public void MenuBar_MapsGodotKeyboardRoutingAcrossOpenPopups()
        {
            var context = new UIContext();
            var bar = new MenuBar { Size = new Vector2(220, 28) };
            var file = bar.AddMenu("File");
            var view = bar.AddMenu("View"); view.Enabled = false;
            var edit = bar.AddMenu("Edit");
            file.Menu.AddItem("Open", 1);
            view.Menu.AddItem("Zoom", 2);
            edit.Menu.AddItem("Undo", 3);
            context.Add(bar);
            context.Layout();
            file.ShowPopup();

            Assert.That(file.Menu.Visible, Is.True);
            Assert.That(context.FocusedControl, Is.SameAs(file.Menu));

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Right));
            context.Update(Time, Mouse(0, 0), new KeyboardState());

            Assert.That(file.Menu.Visible, Is.False);
            Assert.That(view.Menu.Visible, Is.False);
            Assert.That(edit.Menu.Visible, Is.True);
            Assert.That(context.FocusedControl, Is.SameAs(edit.Menu));
            Assert.That(edit.Menu.HighlightedIndex, Is.EqualTo(0));

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Left));
            context.Update(Time, Mouse(0, 0), new KeyboardState());

            Assert.That(edit.Menu.Visible, Is.False);
            Assert.That(file.Menu.Visible, Is.True);
            Assert.That(context.FocusedControl, Is.SameAs(file.Menu));
            Assert.That(file.Menu.HighlightedIndex, Is.EqualTo(0));
        }

        [Test]
        public void MenuBar_MapsGodotShortcutInputActivatingItemsAcrossClosedMenus()
        {
            var context = new UIContext();
            var bar = new MenuBar { Size = new Vector2(220, 28) };
            var file = bar.AddMenu("File");
            var view = bar.AddMenu("View"); view.Visible = false;
            var edit = bar.AddMenu("Edit"); edit.Enabled = false;
            file.Menu.AddItem("Open", 1); file.Menu.SetItemAccelerator(0, new PopupMenuShortcut("Open", Keys.O, control: true));
            view.Menu.AddItem("Zoom", 2); view.Menu.SetItemAccelerator(0, new PopupMenuShortcut("Zoom", Keys.Z, control: true));
            edit.Menu.AddItem("Undo", 3); edit.Menu.SetItemAccelerator(0, new PopupMenuShortcut("Undo", Keys.U, control: true));
            context.Add(bar); context.Layout();

            var fileActivations = 0; file.Menu.IndexPressed += (_, _) => fileActivations++;
            var viewActivations = 0; view.Menu.IndexPressed += (_, _) => viewActivations++;
            var editActivations = 0; edit.Menu.IndexPressed += (_, _) => editActivations++;

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.O));
            Assert.That(fileActivations, Is.EqualTo(1), "Godot's MenuBar::shortcut_input activates a matching accelerator even while every menu is closed.");
            Assert.That(file.Menu.Visible, Is.False, "Activation must not open the popup.");

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.Z));
            Assert.That(viewActivations, Is.EqualTo(0), "A hidden MenuButton's menu is skipped, matching menu_cache[i].hidden.");

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.U));
            Assert.That(editActivations, Is.EqualTo(0), "A disabled MenuButton's menu is skipped, matching menu_cache[i].disabled.");

            bar.DisableShortcuts = true;
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.O));
            Assert.That(fileActivations, Is.EqualTo(1), "disable_shortcuts blocks the bar's own accelerator routing.");
        }

        [Test]
        public void Tree_PreservesHierarchyCollapseAndKeyboardSelection()
        {
            var tree = new Tree { Size = new Vector2(200, 120) };
            var parent = tree.CreateItem(); parent.Text = "Parent";
            var child = parent.CreateChild(); child.Text = "Child";
            tree.Select(parent);
            parent.SetCollapsed(true);
            var context = new UIContext(); context.Add(tree); tree.GrabFocus();
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Down));

            Assert.That(parent.Collapsed, Is.True);
            Assert.That(tree.SelectedItem, Is.SameAs(parent));
            parent.SetCollapsed(false);
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Down));
            Assert.That(tree.SelectedItem, Is.SameAs(child));
        }

        [Test]
        public void Tree_ImplementsGodotRecursiveCollapseVisibilityAndVisibleTraversal()
        {
            var tree = new Tree { Size = new Vector2(200, 120) };
            var root = tree.CreateItem(); root.Text = "Root";
            var branch = root.CreateChild(); branch.Text = "Branch";
            var leaf = branch.CreateChild(); leaf.Text = "Leaf";
            var sibling = root.CreateChild(); sibling.Text = "Sibling";

            root.SetCollapsedRecursive(true);
            Assert.That(root.Collapsed, Is.True);
            Assert.That(branch.Collapsed, Is.True);
            Assert.That(root.IsAnyCollapsed(), Is.True);
            leaf.UncollapseTree();
            Assert.That(root.Collapsed, Is.False);
            Assert.That(branch.Collapsed, Is.False);

            tree.Select(branch);
            branch.SetVisible(false);
            Assert.That(branch.IsVisibleInTree(), Is.False);
            Assert.That(tree.SelectedItem, Is.Null);
            Assert.That(root.GetNextVisible(), Is.SameAs(sibling));
            Assert.That(sibling.GetNextVisible(true), Is.SameAs(root));
            Assert.That(root.IsAnyCollapsed(onlyVisible: true), Is.False);
        }

        [Test]
        public void Tree_TracksPerColumnCellsHeadersAndPointerSelection()
        {
            var tree = new Tree { Size = new Vector2(200, 120), Columns = 2, ColumnTitlesVisible = true };
            tree.SetColumnTitle(0, "Node");
            tree.SetColumnTitle(1, "Type");
            tree.SetColumnCustomMinimumWidth(0, 130);
            tree.SetColumnCustomMinimumWidth(1, 60);
            var item = tree.CreateItem(); item.SetText(0, "Main"); item.SetText(1, "Node2D");
            var selectedColumn = -1;
            tree.CellSelected += (_, selected, column) => { if (selected == item) selectedColumn = column; };
            var context = new UIContext(); context.Add(tree);

            context.Update(Time, Mouse(160, 30), new KeyboardState());
            context.Update(Time, Mouse(160, 30, ButtonState.Pressed), new KeyboardState());

            Assert.That(tree.GetColumnTitle(0), Is.EqualTo("Node"));
            Assert.That(item.GetText(1), Is.EqualTo("Node2D"));
            Assert.That(tree.SelectedItem, Is.SameAs(item));
            Assert.That(tree.SelectedColumn, Is.EqualTo(1));
            Assert.That(selectedColumn, Is.EqualTo(1));
        }

        [Test]
        public void Tree_StoresGodotCustomCellPresentationAndAlignment()
        {
            var tree = new Tree { Columns = 2 };
            var item = tree.CreateItem();
            item.SetCustomColor(0, Color.CornflowerBlue);
            item.SetCustomBackgroundColor(1, Color.DarkSlateGray, outlineOnly: true);
            item.SetTextAlignment(1, HorizontalAlignment.Right);
            item.SetExpandRight(1, true);

            Assert.That(item.GetCustomColor(0), Is.EqualTo(Color.CornflowerBlue));
            Assert.That(item.GetCustomBackgroundColor(1), Is.EqualTo(Color.DarkSlateGray));
            Assert.That(item.IsCustomBackgroundOutline(1), Is.True);
            Assert.That(item.GetTextAlignment(1), Is.EqualTo(HorizontalAlignment.Right));
            Assert.That(item.GetExpandRight(1), Is.True);
            item.ClearCustomColor(0); item.ClearCustomBackgroundColor(1);
            Assert.That(item.GetCustomColor(0), Is.Null);
            Assert.That(item.GetCustomBackgroundColor(1), Is.Null);
        }

        [Test]
        public void Tree_StoresGodotCellIconPresentationState()
        {
            var tree = new Tree { Columns = 2 };
            var item = tree.CreateItem();
            item.SetIcon(0, null); item.SetIconOverlay(0, null); item.SetIconRegion(0, new Rectangle(2, 3, 8, 9)); item.SetIconModulate(0, Color.Orange); item.SetIconMaxWidth(0, 12);

            Assert.That(item.GetIcon(0), Is.Null);
            Assert.That(item.GetIconOverlay(0), Is.Null);
            Assert.That(item.GetIconRegion(0), Is.EqualTo(new Rectangle(2, 3, 8, 9)));
            Assert.That(item.GetIconModulate(0), Is.EqualTo(Color.Orange));
            Assert.That(item.GetIconMaxWidth(0), Is.EqualTo(12));
        }

        [Test]
        public void Tree_MapsGodotColumnHeaderTooltipAndAlignment()
        {
            var tree = new Tree { Size = new Vector2(160, 80), Columns = 2, ColumnTitlesVisible = true };
            tree.SetColumnCustomMinimumWidth(0, 80); tree.SetColumnCustomMinimumWidth(1, 80);
            tree.SetColumnTitle(0, "Node"); tree.SetColumnTitleTooltipText(0, "Scene node name"); tree.SetColumnTitleAlignment(1, HorizontalAlignment.Right);
            var context = new UIContext(); context.Add(tree); context.Layout();

            Assert.That(tree.GetColumnTitleTooltipText(0), Is.EqualTo("Scene node name"));
            Assert.That(tree.GetColumnTitleAlignment(0), Is.EqualTo(HorizontalAlignment.Center));
            Assert.That(tree.GetColumnTitleAlignment(1), Is.EqualTo(HorizontalAlignment.Right));
            Assert.That(tree.GetTooltip(new Point(10, 10)), Is.EqualTo("Scene node name"));
        }

        [Test]
        public void Tree_MapsGodotColumnHeaderDirectionAndLanguage()
        {
            var tree = new Tree { Columns = 2 };

            Assert.That(tree.GetColumnTitleDirection(0), Is.EqualTo(TextDirection.Inherited));
            Assert.That(tree.GetColumnTitleLanguage(0), Is.EqualTo(string.Empty));

            tree.SetColumnTitleDirection(1, TextDirection.RightToLeft);
            tree.SetColumnTitleLanguage(1, "ar");

            Assert.That(tree.GetColumnTitleDirection(1), Is.EqualTo(TextDirection.RightToLeft));
            Assert.That(tree.GetColumnTitleLanguage(1), Is.EqualTo("ar"));
            tree.SetColumnTitleLanguage(1, null);
            Assert.That(tree.GetColumnTitleLanguage(1), Is.EqualTo(string.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => tree.SetColumnTitleDirection(2, TextDirection.Auto));
        }

        [Test]
        public void Tree_MapsGodotAutomaticCellTooltipPrecedence()
        {
            var tree = new Tree { Size = new Vector2(120, 50) };
            var item = tree.CreateItem(); item.Text = "Camera2D";
            var context = new UIContext(); context.Add(tree); context.Layout();

            Assert.That(tree.IsAutoTooltipEnabled(), Is.True);
            Assert.That(tree.GetTooltip(new Point(10, 10)), Is.EqualTo("Camera2D"));
            item.SetTooltipText(0, "Camera node");
            Assert.That(tree.GetTooltip(new Point(10, 10)), Is.EqualTo("Camera node"));
            tree.SetAutoTooltip(false); item.SetTooltipText(0, string.Empty);
            Assert.That(tree.GetTooltip(new Point(10, 10)), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Tree_MapsGodotCheckboxOnlyEditAndHiddenFoldingPolicies()
        {
            var tree = new Tree { Size = new Vector2(120, 60) };
            var root = tree.CreateItem(); root.SetCellMode(0, TreeCellMode.Check); root.SetEditable(0, true); root.CreateChild().Text = "Child";
            var context = new UIContext(); context.Add(tree); context.Layout();

            context.Update(Time, Mouse(50, 10), new KeyboardState());
            context.Update(Time, Mouse(50, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(50, 10), new KeyboardState());
            Assert.That(root.IsChecked(0), Is.True);

            tree.SetEditCheckboxCellOnlyWhenCheckboxPressed(true);
            context.Update(Time, Mouse(50, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(50, 10), new KeyboardState());
            Assert.That(root.IsChecked(0), Is.True);
            context.Update(Time, Mouse(20, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(20, 10), new KeyboardState());
            Assert.That(root.IsChecked(0), Is.False);
            Assert.That(tree.IsEditCheckboxCellOnlyWhenCheckboxPressed(), Is.True);

            tree.SetHideFolding(true);
            context.Update(Time, Mouse(5, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(5, 10), new KeyboardState());
            Assert.That(root.Collapsed, Is.False);
            tree.SetHideFolding(false);
            context.Update(Time, Mouse(5, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(5, 10), new KeyboardState());
            Assert.That(root.Collapsed, Is.True);
            Assert.That(tree.IsFoldingHidden(), Is.False);

            root.SetCollapsedRecursive(false);
            var nested = root.GetChild(0); nested.CreateChild().Text = "Leaf";
            tree.SetEnableRecursiveFolding(true);
            context.Update(Time, Mouse(5, 10, ButtonState.Pressed), new KeyboardState(Keys.LeftShift));
            context.Update(Time, Mouse(5, 10), new KeyboardState());
            Assert.That(root.Collapsed, Is.True);
            Assert.That(nested.Collapsed, Is.True);
            tree.SetEnableRecursiveFolding(false);
            Assert.That(tree.IsRecursiveFoldingEnabled(), Is.False);
        }

        [Test]
        public void Tree_MapsGodotVerticalScrollAndItemReveal()
        {
            var tree = new Tree { Size = new Vector2(120, 80), ItemHeight = 20, ColumnTitlesVisible = true };
            var items = new List<TreeItem>();
            for (var index = 0; index < 5; index++) { var item = tree.CreateItem(); item.Text = $"Row {index}"; items.Add(item); }
            var context = new UIContext(); context.Add(tree); context.Layout();

            Assert.That(tree.GetScroll(), Is.EqualTo(Vector2.Zero));
            tree.ScrollToItem(items[4]);
            Assert.That(tree.GetScroll().Y, Is.GreaterThan(0));
            var lastRect = tree.GetItemAreaRectangle(items[4]);
            Assert.That(lastRect.Top, Is.GreaterThanOrEqualTo(21));
            Assert.That(lastRect.Bottom, Is.LessThanOrEqualTo(79));
            Assert.That(tree.GetItemAtPosition(new Point(10, lastRect.Center.Y)), Is.SameAs(items[4]));

            tree.ScrollToItem(items[0]);
            context.Update(Time, Mouse(10, 30), new KeyboardState());
            context.Update(Time, Mouse(10, 30, scrollWheel: -120), new KeyboardState());
            Assert.That(tree.GetScroll().Y, Is.EqualTo(20));

            tree.Select(items[4]);
            tree.EnsureCursorIsVisible();
            Assert.That(tree.GetItemAreaRectangle(items[4]).Bottom, Is.LessThanOrEqualTo(79));
            tree.SetVerticalScrollEnabled(false);
            Assert.That(tree.GetScroll(), Is.EqualTo(Vector2.Zero));
            context.Update(Time, Mouse(10, 30, scrollWheel: -240), new KeyboardState());
            Assert.That(tree.GetScroll(), Is.EqualTo(Vector2.Zero));
            tree.SetHorizontalScrollEnabled(false);
            Assert.That(tree.IsHorizontalScrollEnabled(), Is.False);
            Assert.That(tree.IsVerticalScrollEnabled(), Is.False);

            tree.SetVerticalScrollEnabled(true);
            tree.ScrollToItem(items[0]); tree.Select(items[2]); tree.KeyPressed(Keys.Down);
            Assert.That(tree.SelectedItem, Is.SameAs(items[3]));
            Assert.That(tree.GetScroll().Y, Is.GreaterThan(0));
        }

        [Test]
        public void Tree_MapsGodotTouchDragScrollAndDeceleration()
        {
            var tree = new Tree { Size = new Vector2(120, 80), ItemHeight = 20 };
            for (var index = 0; index < 20; index++) { var item = tree.CreateItem(); item.Text = $"Row {index}"; }
            var context = new UIContext(); context.Add(tree); context.Layout();

            tree.BeginTouchDragScroll();
            Assert.That(tree.IsTouchDragging, Is.True);

            tree.TouchDragScrollBy(relativeMotion: -30, velocity: -200);
            Assert.That(tree.GetScroll().Y, Is.EqualTo(30), "Godot's Tree accumulates the negated relative motion directly onto the scroll origin.");
            Assert.That(tree.TouchDragSpeed, Is.EqualTo(200).Within(.001f), "Speed is taken directly from the motion event's velocity, negated, unlike ScrollContainer's sampled speed.");

            tree.EndTouchDragScroll();
            Assert.That(tree.IsTouchDragDecelerating, Is.True);
            tree.Process(new GameTime(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)));
            Assert.That(tree.GetScroll().Y, Is.EqualTo(40).Within(.001f));
            Assert.That(tree.TouchDragSpeed, Is.EqualTo(150).Within(.001f));

            tree.Process(new GameTime(TimeSpan.FromMilliseconds(450), TimeSpan.FromMilliseconds(400)));
            Assert.That(tree.GetScroll().Y, Is.EqualTo(100).Within(.001f), "Deceleration keeps moving right up to the frame where speed finally crosses zero.");
            Assert.That(tree.IsTouchDragging, Is.False, "Unlike ScrollContainer's two-axis AND check, Tree's single-axis drag cancels as soon as speed crosses zero.");
            Assert.That(tree.IsTouchDragDecelerating, Is.False);

            tree.BeginTouchDragScroll();
            tree.TouchDragScrollBy(relativeMotion: 5, velocity: 0);
            tree.EndTouchDragScroll();
            Assert.That(tree.IsTouchDragging, Is.False, "A drag released with zero speed cancels immediately without a deceleration phase.");
            Assert.That(tree.IsTouchDragDecelerating, Is.False);
        }

        [Test]
        public void Tree_MapsGodotVerticalScrollBarAndHintPolicies()
        {
            var tree = new Tree { Size = new Vector2(120, 80), ItemHeight = 20, ColumnTitlesVisible = true };
            for (var index = 0; index < 5; index++) tree.CreateItem().Text = $"Row {index}";
            var context = new UIContext(); context.Add(tree); context.Layout();

            var scrollBar = tree.GetVScrollBar();
            Assert.That(tree.IsVerticalScrollBarVisible, Is.True);
            Assert.That(scrollBar.Bounds, Is.EqualTo(new Rectangle(106, 0, 14, 80)));
            Assert.That(scrollBar.Page, Is.EqualTo(58));
            Assert.That(tree.GetItemAreaRectangle(tree.GetRoot()).Width, Is.EqualTo(104), "Rows reserve the visible scrollbar lane.");
            Assert.That(tree.GetItemAtPosition(scrollBar.Bounds.Center), Is.Null);
            scrollBar.Value = 42;
            Assert.That(tree.GetScroll().Y, Is.EqualTo(42));

            tree.SetScrollHintMode(TreeScrollHintMode.Both); tree.SetTileScrollHint(true);
            Assert.That(tree.GetScrollHintMode(), Is.EqualTo(TreeScrollHintMode.Both));
            Assert.That(tree.IsScrollHintTiled(), Is.True);
            tree.SetVerticalScrollEnabled(false);
            Assert.That(tree.IsVerticalScrollBarVisible, Is.False);
            Assert.That(tree.GetScroll(), Is.EqualTo(Vector2.Zero));
        }

        [Test]
        public void Tree_MapsGodotHorizontalOverflowAndCombinedScrollbarLayout()
        {
            var tree = new Tree { Size = new Vector2(120, 100), ItemHeight = 20, ColumnTitlesVisible = true, Columns = 2, HideRoot = true };
            tree.SetColumnCustomMinimumWidth(0, 90); tree.SetColumnCustomMinimumWidth(1, 90);
            tree.CreateItem();
            var rows = new List<TreeItem>();
            for (var index = 0; index < 5; index++) { var item = tree.CreateItem(); item.SetText(0, $"Row {index}"); item.SetText(1, "Type"); rows.Add(item); }
            tree.Indent = 100;
            var nested = rows[0].CreateChild(); nested.Text = "Nested";
            nested.CreateChild().Text = "Leaf";
            var context = new UIContext(); context.Add(tree); context.Layout();

            var horizontal = tree.GetHScrollBar(); var vertical = tree.GetVScrollBar();
            Assert.That(tree.IsHorizontalScrollBarVisible, Is.True);
            Assert.That(tree.IsVerticalScrollBarVisible, Is.True);
            Assert.That(horizontal.Bounds, Is.EqualTo(new Rectangle(0, 86, 106, 14)));
            Assert.That(vertical.Bounds, Is.EqualTo(new Rectangle(106, 0, 14, 86)));
            Assert.That(horizontal.Page, Is.EqualTo(104));
            Assert.That(vertical.Page, Is.EqualTo(64));
            Assert.That(tree.GetColumnWidth(0), Is.EqualTo(90));
            Assert.That(tree.GetColumnWidth(1), Is.EqualTo(90));

            horizontal.Value = 76;
            Assert.That(tree.GetScroll().X, Is.EqualTo(76));
            Assert.That(tree.GetItemAreaRectangle(rows[0], 1).X, Is.EqualTo(15));
            Assert.That(tree.GetColumnAtPosition(new Point(90, 30)), Is.EqualTo(1));
            var nestedArea = tree.GetItemAreaRectangle(nested);
            var foldingPoint = new Point(tree.Bounds.X + 1 - (int)tree.GetScroll().X + (int)tree.Indent + 4, nestedArea.Center.Y);
            context.Update(Time, Mouse(foldingPoint.X, foldingPoint.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(foldingPoint.X, foldingPoint.Y), new KeyboardState());
            Assert.That(nested.Collapsed, Is.True, "The folding hit target follows the scrolled horizontal content position.");
            horizontal.Value = 0;
            tree.Select(rows[0], 1); tree.EnsureCursorIsVisible();
            Assert.That(tree.GetScroll().X, Is.GreaterThan(0), "Cursor reveal scrolls the selected offscreen column into view.");

            tree.SetHorizontalScrollEnabled(false);
            Assert.That(tree.IsHorizontalScrollBarVisible, Is.False);
            Assert.That(tree.GetScroll().X, Is.Zero);
        }

        [Test]
        public void Tree_MapsGodotApplicationDragPayloadHooksAcrossTrees()
        {
            var source = new Tree { Size = new Vector2(100, 70) };
            var sourceItem = source.CreateItem(); sourceItem.Text = "Camera"; sourceItem.SetMetadata(0, "node/camera");
            var requestedItem = default(TreeItem); var requestedColumn = -2;
            source.DragDataProvider = (_, item, column, _) => { requestedItem = item; requestedColumn = column; return item.GetMetadata(0); };

            var target = new Tree { Position = new Vector2(140, 0), Size = new Vector2(100, 70) };
            var targetItem = target.CreateItem(); targetItem.Text = "Drop here";
            object delivered = null; TreeItem deliveredTarget = null; var accepted = 0;
            target.CanDropDataProvider = (_, item, column, _, data) => { accepted++; return item == targetItem && column == 0 && Equals(data, "node/camera"); };
            target.DropDataHandler = (_, item, column, _, data) => { deliveredTarget = item; delivered = data; Assert.That(column, Is.EqualTo(0)); };

            var context = new UIContext(); context.Add(source); context.Add(target); context.Layout();
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(150, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(150, 10), new KeyboardState());

            Assert.That(requestedItem, Is.SameAs(sourceItem));
            Assert.That(requestedColumn, Is.EqualTo(0));
            Assert.That(accepted, Is.GreaterThan(0));
            Assert.That(deliveredTarget, Is.SameAs(targetItem));
            Assert.That(delivered, Is.EqualTo("node/camera"));
        }

        [Test]
        public void Tree_MapsGodotDragEdgeAutoScrollLifecycle()
        {
            var tree = new Tree { Size = new Vector2(120, 80), ItemHeight = 20, SelfDragDropEnabled = true, DragAutoScrollBorder = 16, DragAutoScrollSpeed = 240, HideRoot = true };
            tree.CreateItem();
            for (var index = 0; index < 8; index++) { var item = tree.CreateItem(); item.Text = $"Row {index}"; }
            var context = new UIContext(); context.Add(tree); context.Layout();
            var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(100));

            context.Update(frame, Mouse(10, 10), new KeyboardState());
            context.Update(frame, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(frame, Mouse(10, 79, ButtonState.Pressed), new KeyboardState());
            context.Update(frame, Mouse(10, 79, ButtonState.Pressed), new KeyboardState());
            Assert.That(tree.GetScroll().Y, Is.GreaterThan(0));

            var scrolled = tree.GetScroll().Y;
            context.Update(frame, Mouse(10, 79), new KeyboardState());
            context.Update(frame, Mouse(10, 79), new KeyboardState());
            Assert.That(tree.GetScroll().Y, Is.EqualTo(scrolled));
        }

        [Test]
        public void Tree_MapsGodotDelayedDragUnfolding()
        {
            var tree = new Tree { Size = new Vector2(120, 90), ItemHeight = 20, SelfDragDropEnabled = true, DragUnfoldDelay = TimeSpan.FromMilliseconds(500), HideRoot = true };
            tree.CreateItem();
            var source = tree.CreateItem(); source.Text = "Source";
            var target = tree.CreateItem(); target.Text = "Collapsed"; target.CreateChild().Text = "Hidden"; target.SetCollapsed(true);
            var context = new UIContext(); context.Add(tree); context.Layout();
            var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(200));

            context.Update(frame, Mouse(10, 10), new KeyboardState());
            context.Update(frame, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(frame, Mouse(10, 30, ButtonState.Pressed), new KeyboardState());
            Assert.That(target.Collapsed, Is.True);
            context.Update(frame, Mouse(10, 30, ButtonState.Pressed), new KeyboardState());
            Assert.That(target.Collapsed, Is.True);
            context.Update(frame, Mouse(10, 30, ButtonState.Pressed), new KeyboardState());
            Assert.That(target.Collapsed, Is.False);

            target.SetCollapsed(true); tree.SetEnableDragUnfolding(false);
            context.Update(frame, Mouse(10, 30, ButtonState.Pressed), new KeyboardState());
            context.Update(frame, Mouse(10, 30, ButtonState.Pressed), new KeyboardState());
            Assert.That(target.Collapsed, Is.True);
            context.Update(frame, Mouse(10, 30), new KeyboardState());
        }

        [Test]
        public void Tree_MapsGodotCustomCellEditStateAndPopupAnchor()
        {
            var tree = new Tree { Size = new Vector2(120, 50) };
            var item = tree.CreateItem(); item.SetCellMode(0, TreeCellMode.Custom); item.SetEditable(0, true);
            var popupArrowStates = new List<bool>(); var edits = 0;
            tree.CustomPopupEdited += (_, arrowPressed) => popupArrowStates.Add(arrowPressed);
            tree.ItemEdited += (_, _, _) => edits++;
            var context = new UIContext(); context.Add(tree); context.Layout();

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());

            var cell = tree.GetItemAreaRectangle(item, 0);
            Assert.That(tree.GetEdited(), Is.SameAs(item));
            Assert.That(tree.GetEditedColumn(), Is.EqualTo(0));
            Assert.That(tree.IsEditing(), Is.True);
            Assert.That(tree.GetCustomPopupRect(), Is.EqualTo(cell));
            Assert.That(popupArrowStates, Is.EqualTo(new[] { false }));
            Assert.That(edits, Is.EqualTo(1));
            tree.Select(item);
            Assert.That(tree.EditSelected(), Is.True);
            item.SetCustomAsButton(0, true);
            context.Update(Time, Mouse(cell.Right - 1, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(cell.Right - 1, 10), new KeyboardState());
            Assert.That(popupArrowStates, Is.EqualTo(new[] { false, false, true }));
            Assert.That(edits, Is.EqualTo(2));
            context.Update(Time, Mouse(cell.X + 2, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(cell.X + 2, 10), new KeyboardState());
            Assert.That(popupArrowStates, Is.EqualTo(new[] { false, false, true }));
            Assert.That(edits, Is.EqualTo(3));
            item.SetCustomAsButton(0, false);
            context.Update(Time, Mouse(cell.Right - 1, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(cell.Right - 1, 10), new KeyboardState());
            Assert.That(popupArrowStates, Is.EqualTo(new[] { false, false, true, true }));
            Assert.That(edits, Is.EqualTo(4));
            tree.KeyPressed(Keys.Enter);
            Assert.That(popupArrowStates, Is.EqualTo(new[] { false, false, true, true, false }));
            Assert.That(edits, Is.EqualTo(5));
        }

        [Test]
        public void Tree_MapsGodotColumnClippingWidthAndHeaderClickSignal()
        {
            var tree = new Tree { Size = new Vector2(162, 80), Columns = 2, ColumnTitlesVisible = true };
            tree.SetColumnCustomMinimumWidth(0, 60); tree.SetColumnCustomMinimumWidth(1, 100);
            tree.SetColumnClipContent(0, true);
            var clickedColumn = -1; var clickedButton = PointerButton.None; tree.ColumnTitleClicked += (_, column, button) => { clickedColumn = column; clickedButton = button; };
            var context = new UIContext(); context.Add(tree); context.Layout();

            context.Update(Time, Mouse(20, 10), new KeyboardState());
            context.Update(Time, Mouse(20, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(20, 10), new KeyboardState());

            Assert.That(tree.IsColumnClippingContent(0), Is.True);
            Assert.That(tree.IsColumnClippingContent(1), Is.False);
            Assert.That(tree.GetColumnWidth(0), Is.EqualTo(60));
            Assert.That(tree.GetColumnWidth(1), Is.EqualTo(100));
            Assert.That(clickedColumn, Is.EqualTo(0));
            Assert.That(clickedButton, Is.EqualTo(PointerButton.Left));

            context.Update(Time, Mouse(120, 10, right: ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(120, 10), new KeyboardState());
            Assert.That(clickedColumn, Is.EqualTo(1));
            Assert.That(clickedButton, Is.EqualTo(PointerButton.Right));

            context.Update(Time, Mouse(20, 10, middle: ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(20, 10), new KeyboardState());
            Assert.That(clickedButton, Is.EqualTo(PointerButton.Right), "Godot emits column_title_clicked only for left and right buttons.");
        }

        [Test]
        public void TreeItem_MapsGodotSiblingMovementAndReparenting()
        {
            var tree = new Tree();
            var root = tree.CreateItem(); root.SetText(0, "Root");
            var group = root.CreateChild(); group.SetText(0, "Group");
            var camera = group.CreateChild(); camera.SetText(0, "Camera");
            var cameraChild = camera.CreateChild(); cameraChild.SetText(0, "CameraChild");
            var sprite = root.CreateChild(); sprite.SetText(0, "Sprite");

            camera.MoveAfter(sprite);
            Assert.That(camera.Parent, Is.SameAs(root));
            Assert.That(root.Children, Is.EqualTo(new[] { group, sprite, camera }));
            Assert.That(camera.Children, Is.EqualTo(new[] { cameraChild }));
            Assert.That(camera.GetPrevious(), Is.SameAs(sprite));
            Assert.That(sprite.GetNext(), Is.SameAs(camera));
            Assert.That(camera.GetIndex(), Is.EqualTo(2));
            Assert.That(root.GetChild(-1), Is.SameAs(camera));

            sprite.MoveBefore(group);
            Assert.That(root.Children, Is.EqualTo(new[] { sprite, group, camera }));
            Assert.That(sprite.GetIndex(), Is.EqualTo(0));
            Assert.That(() => root.MoveBefore(cameraChild), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Tree_MapsGodotItemColumnAndDropSectionHitTesting()
        {
            var tree = new Tree { Size = new Vector2(160, 100), Columns = 2, DropModeFlags = TreeDropModeFlags.OnItem | TreeDropModeFlags.InBetween };
            tree.SetColumnCustomMinimumWidth(0, 80); tree.SetColumnCustomMinimumWidth(1, 80);
            var root = tree.CreateItem(); root.SetText(0, "Root");
            var child = root.CreateChild(); child.SetText(0, "Child"); child.SetAcceptChildren(false);
            var context = new UIContext(); context.Add(tree); context.Layout();
            var rootArea = tree.GetItemAreaRectangle(root); var childArea = tree.GetItemAreaRectangle(child);

            Assert.That(tree.GetItemAtPosition(new Point(childArea.X + 10, childArea.Center.Y)), Is.SameAs(child));
            Assert.That(tree.GetColumnAtPosition(new Point(childArea.X + 10, childArea.Center.Y)), Is.EqualTo(0));
            Assert.That(tree.GetColumnAtPosition(new Point(childArea.Right - 10, childArea.Center.Y)), Is.EqualTo(1));
            Assert.That(tree.GetDropSectionAtPosition(rootArea.Center), Is.EqualTo(0));
            Assert.That(tree.GetDropSectionAtPosition(new Point(childArea.Center.X, childArea.Y + 1)), Is.EqualTo(-1));
            Assert.That(tree.GetDropSectionAtPosition(new Point(childArea.Center.X, childArea.Bottom - 1)), Is.EqualTo(1));
            tree.DropModeFlags = TreeDropModeFlags.OnItem;
            Assert.That(tree.GetDropSectionAtPosition(childArea.Center), Is.EqualTo(Tree.DropSectionNotFound));
            tree.DropModeFlags = TreeDropModeFlags.Disabled;
            Assert.That(tree.GetDropSectionAtPosition(rootArea.Center), Is.EqualTo(Tree.DropSectionNotFound));
        }

        [Test]
        public void Tree_MapsGodotInteractiveColumnHeaderResize()
        {
            var tree = new Tree { Size = new Vector2(200, 100), Columns = 2, ColumnTitlesVisible = true };
            tree.SetColumnTitle(0, "Name"); tree.SetColumnTitle(1, "Type");
            var resizedColumn = -1; var resizedWidth = -1;
            tree.ColumnResized += (_, column, width) => { resizedColumn = column; resizedWidth = width; };
            var context = new UIContext(); context.Add(tree); context.Layout();
            var divider = tree.Bounds.X + 1 + tree.GetColumnWidth(0);

            context.Update(Time, Mouse(divider, tree.Bounds.Y + 8), new KeyboardState());
            context.Update(Time, Mouse(divider, tree.Bounds.Y + 8, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(divider + 40, tree.Bounds.Y + 8, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(divider + 40, tree.Bounds.Y + 8), new KeyboardState());

            Assert.That(resizedColumn, Is.EqualTo(0));
            Assert.That(resizedWidth, Is.EqualTo(139));
            Assert.That(tree.GetColumnWidth(0), Is.EqualTo(139));
            Assert.That(tree.IsColumnExpanding(0), Is.False);
        }

        [Test]
        public void Tree_MapsOptInSameTreeDragDropReparenting()
        {
            var tree = new Tree { Size = new Vector2(160, 100), HideRoot = true, DropModeFlags = TreeDropModeFlags.OnItem | TreeDropModeFlags.InBetween, SelfDragDropEnabled = true };
            var root = tree.CreateItem();
            var first = root.CreateChild(); first.Text = "First";
            var second = root.CreateChild(); second.Text = "Second";
            var dropped = false;
            tree.ItemDropped += (_, item, target, section) => dropped = item == first && target == second && section == 1;
            var context = new UIContext(); context.Add(tree); context.Layout();
            var firstArea = tree.GetItemAreaRectangle(first);
            var secondArea = tree.GetItemAreaRectangle(second);

            context.Update(Time, Mouse(firstArea.Center.X, firstArea.Center.Y), new KeyboardState());
            context.Update(Time, Mouse(firstArea.Center.X, firstArea.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(secondArea.Center.X, secondArea.Bottom - 1, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(secondArea.Center.X, secondArea.Bottom - 1), new KeyboardState());

            Assert.That(dropped, Is.True);
            Assert.That(root.GetChild(0), Is.EqualTo(second));
            Assert.That(root.GetChild(1), Is.EqualTo(first));
            Assert.That(first.GetParent(), Is.EqualTo(root));
            Assert.That(tree.CanDropData(secondArea.Center, root), Is.False);
        }

        [Test]
        public void TreeItem_MapsGodotExplicitCheckPropagation()
        {
            var tree = new Tree();
            var root = tree.CreateItem();
            var left = root.CreateChild(); var leftLeaf = left.CreateChild();
            var right = root.CreateChild();
            var propagated = new List<TreeItem>(); tree.CheckPropagatedToItem += (_, item, _) => propagated.Add(item);

            root.SetChecked(0, true); root.PropagateCheck(0);
            Assert.That(new[] { root.IsChecked(0), left.IsChecked(0), leftLeaf.IsChecked(0), right.IsChecked(0) }, Is.EqualTo(new[] { true, true, true, true }));
            Assert.That(root.IsIndeterminate(0), Is.False);

            left.SetChecked(0, false); left.PropagateCheck(0);
            Assert.That(leftLeaf.IsChecked(0), Is.False);
            Assert.That(root.IsChecked(0), Is.False);
            Assert.That(root.IsIndeterminate(0), Is.True);

            right.SetChecked(0, false); right.PropagateCheck(0);
            Assert.That(root.IsChecked(0), Is.False);
            Assert.That(root.IsIndeterminate(0), Is.False);
            Assert.That(propagated, Does.Contain(root));
            Assert.That(propagated, Does.Contain(leftLeaf));
        }

        [Test]
        public void TreeItem_MapsGodotCellActionButtonsAndClickSignal()
        {
            var tree = new Tree { Size = new Vector2(160, 30) };
            var item = tree.CreateItem(); item.SetText(0, "Camera");
            item.AddButton(0, null, 10, tooltip: "Frame Camera", description: "Focuses the camera");
            item.AddButton(0, null, 20, disabled: true, tooltip: "Locked");
            var clicks = 0; var clickedId = -1;
            tree.ButtonClicked += (_, clickedItem, column, id) => { clicks++; clickedId = id; Assert.That(clickedItem, Is.SameAs(item)); Assert.That(column, Is.EqualTo(0)); };
            var context = new UIContext(); context.Add(tree); context.Layout();
            var first = tree.GetItemAreaRectangle(item, 0, 0); var disabled = tree.GetItemAreaRectangle(item, 0, 1);

            context.Update(Time, Mouse(first.Center.X, first.Center.Y), new KeyboardState());
            context.Update(Time, Mouse(first.Center.X, first.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(first.Center.X, first.Center.Y), new KeyboardState());
            context.Update(Time, Mouse(disabled.Center.X, disabled.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(disabled.Center.X, disabled.Center.Y), new KeyboardState());

            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(clickedId, Is.EqualTo(10));
            Assert.That(tree.SelectedItem, Is.Null);
            Assert.That(tree.GetTooltip(first.Center), Is.EqualTo("Frame Camera"));
            Assert.That(tree.GetButtonIdAtPosition(first.Center), Is.EqualTo(10));
            Assert.That(tree.GetButtonIdAtPosition(disabled.Center), Is.EqualTo(20));
            Assert.That(tree.GetButtonIdAtPosition(new Point(tree.Bounds.Left, tree.Bounds.Top)), Is.EqualTo(-1));
            Assert.That(tree.GetButtonIdAtPosition(new Point(tree.Bounds.Right + 1, tree.Bounds.Bottom + 1)), Is.EqualTo(-1));
            Assert.That(item.GetButtonCount(0), Is.EqualTo(2));
            Assert.That(item.GetButtonId(0, 0), Is.EqualTo(10));
            Assert.That(item.GetButtonById(0, 20), Is.EqualTo(1));
            Assert.That(item.GetButtonDescription(0, 0), Is.EqualTo("Focuses the camera"));
            Assert.That(item.IsButtonDisabled(0, 1), Is.True);
            item.SetButtonColor(0, 0, Color.Orange); item.SetButtonTooltipText(0, 0, "Updated"); item.SetButtonDisabled(0, 1, false);
            Assert.That(item.GetButtonColor(0, 0), Is.EqualTo(Color.Orange));
            Assert.That(tree.GetTooltip(first.Center), Is.EqualTo("Updated"));
            Assert.That(item.IsButtonDisabled(0, 1), Is.False);
            item.EraseButton(0, 0);
            Assert.That(item.GetButtonCount(0), Is.EqualTo(1));
            Assert.That(item.GetButtonId(0, 0), Is.EqualTo(20));
            Assert.Throws<ArgumentOutOfRangeException>(() => item.EraseButton(0, 1));
            item.ClearButtons();
            Assert.That(item.GetButtonCount(0), Is.Zero);
        }

        [Test]
        public void Tree_MapsGodotHierarchySelectionTraversalOffsetsAndPressedButton()
        {
            var tree = new Tree { Size = new Vector2(160, 120), ColumnTitlesVisible = true, SelectMode = TreeSelectMode.Multi };
            var root = tree.CreateItem(); root.Text = "Root";
            var first = tree.CreateItem(root); first.Text = "First";
            var second = tree.CreateItem(root); second.Text = "Second";
            var inserted = tree.CreateItem(root, 0); inserted.Text = "Inserted";
            root.AddButton(0, null, 42);
            var context = new UIContext(); context.Add(tree); context.Layout();

            Assert.That(tree.GetRoot(), Is.SameAs(root));
            Assert.That(root.GetChild(0), Is.SameAs(inserted));
            Assert.That(root.GetChild(1), Is.SameAs(first));
            Assert.That(tree.GetLastItem(), Is.SameAs(second));
            Assert.That(tree.GetItemOffset(root), Is.EqualTo(tree.GetItemAreaRectangle(root).Y - tree.Bounds.Y));
            Assert.That(tree.GetItemOffset(second), Is.EqualTo(tree.GetItemAreaRectangle(second).Y - tree.Bounds.Y));

            first.Select(); second.Select(setAsCursor: false);
            root.SetCollapsed(true);
            Assert.That(tree.GetNextSelected(), Is.SameAs(first), "Selection traversal includes collapsed descendants.");
            Assert.That(tree.GetNextSelected(first), Is.SameAs(second));
            Assert.That(tree.GetNextSelected(second), Is.Null);
            Assert.That(tree.GetLastItem(), Is.SameAs(root), "Last-item traversal stops at a collapsed branch.");
            Assert.That(tree.GetItemOffset(second), Is.Zero, "Item-offset traversal stops at a collapsed branch.");

            root.SetCollapsed(false);
            var button = tree.GetItemAreaRectangle(root, 0, 0);
            context.Update(Time, Mouse(button.Center.X, button.Center.Y), new KeyboardState());
            context.Update(Time, Mouse(button.Center.X, button.Center.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(tree.GetPressedButton(), Is.Zero);
            context.Update(Time, Mouse(button.Center.X, button.Center.Y), new KeyboardState());
            Assert.That(tree.GetPressedButton(), Is.EqualTo(-1));
            tree.SetHideRoot(true); tree.SetColumnTitlesVisible(false);
            Assert.That(tree.IsRootHidden(), Is.True);
            Assert.That(tree.AreColumnTitlesVisible(), Is.False);
        }

        [Test]
        public void Tree_CreateItemWithoutParentKeepsOneRootAndAddsChildrenLikeGodot()
        {
            var tree = new Tree();
            var root = tree.CreateItem();
            var appended = tree.CreateItem();
            var inserted = tree.CreateItem(index: 0);

            Assert.That(tree.GetRoot(), Is.SameAs(root));
            Assert.That(tree.RootItems, Has.Count.EqualTo(1));
            Assert.That(root.GetChildCount(), Is.EqualTo(2));
            Assert.That(root.GetChild(0), Is.SameAs(inserted));
            Assert.That(root.GetChild(1), Is.SameAs(appended));
            Assert.That(appended.Parent, Is.SameAs(root));
        }

        [Test]
        public void TreeItem_MapsGodotPerItemFoldingAndChildLifecycle()
        {
            var tree = new Tree { Size = new Vector2(150, 100) };
            var root = tree.CreateItem(); root.Text = "Root";
            var visibleChild = root.CreateChild(); visibleChild.Text = "Visible";
            var hiddenChild = root.CreateChild(); hiddenChild.Text = "Hidden"; hiddenChild.SetVisible(false);
            var context = new UIContext(); context.Add(tree); context.Layout();

            Assert.That(root.GetChildCount(), Is.EqualTo(2));
            Assert.That(root.GetVisibleChildCount(), Is.EqualTo(1));
            root.SetDisableFolding(true);
            Assert.That(root.IsFoldingDisabled(), Is.True);
            var rootArea = tree.GetItemAreaRectangle(root);
            context.Update(Time, Mouse(rootArea.X + 4, rootArea.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(rootArea.X + 4, rootArea.Center.Y), new KeyboardState());
            Assert.That(root.Collapsed, Is.False, "Per-item folding disablement suppresses the pointer affordance only.");
            root.SetCollapsed(true);
            Assert.That(root.Collapsed, Is.True, "Programmatic collapse remains available.");
            root.SetCollapsed(false);

            tree.Select(visibleChild);
            root.ClearChildren();
            Assert.That(root.GetChildCount(), Is.Zero);
            Assert.That(root.GetVisibleChildCount(), Is.Zero);
            Assert.That(tree.IsAnythingSelected(), Is.False, "Clearing a selected descendant clears the retained cursor.");
        }

        [Test]
        public void TreeItem_MapsGodotFullAndVisibleHierarchyTraversal()
        {
            var tree = new Tree();
            var root = tree.CreateItem(); root.Text = "Root";
            var first = root.CreateChild(); first.Text = "First";
            var leaf = first.CreateChild(); leaf.Text = "Leaf";
            var second = root.CreateChild(); second.Text = "Second";
            var hidden = root.CreateChild(); hidden.Text = "Hidden"; hidden.SetVisible(false);

            Assert.That(root.GetTree(), Is.SameAs(tree));
            Assert.That(root.GetFirstChild(), Is.SameAs(first));
            Assert.That(root.GetLastChild(), Is.SameAs(hidden));
            Assert.That(first.GetNextInTree(), Is.SameAs(leaf));
            Assert.That(leaf.GetPreviousInTree(), Is.SameAs(first));
            Assert.That(second.GetNextInTree(), Is.SameAs(hidden), "Full-tree traversal includes invisible descendants.");
            Assert.That(hidden.GetNextInTree(wrap: true), Is.SameAs(root));

            Assert.That(first.GetNextVisible(), Is.SameAs(leaf));
            Assert.That(second.GetPreviousVisible(), Is.SameAs(leaf));
            first.SetCollapsed(true);
            Assert.That(first.GetNextVisible(), Is.SameAs(second), "Visible traversal skips collapsed descendants.");
            Assert.That(second.GetPreviousVisible(), Is.SameAs(first));
            Assert.That(root.GetPreviousVisible(wrap: true), Is.SameAs(second));
        }

        [Test]
        public void TreeItem_MapsGodotCustomMinimumHeightAndRowAwareInput()
        {
            var tree = new Tree { Size = new Vector2(160, 90), ItemHeight = 20 };
            var first = tree.CreateItem(); first.SetText(0, "Tall row"); first.SetCustomMinimumHeight(42);
            var second = tree.CreateItem(); second.SetText(0, "Default row"); second.SetTooltipText(0, "Second row");
            var context = new UIContext(); context.Add(tree); context.Layout();

            var firstArea = tree.GetItemAreaRectangle(first);
            var secondArea = tree.GetItemAreaRectangle(second);
            Assert.That(first.GetCustomMinimumHeight(), Is.EqualTo(42));
            Assert.That(firstArea, Is.EqualTo(new Rectangle(1, 1, 158, 42)));
            Assert.That(secondArea, Is.EqualTo(new Rectangle(1, 43, 158, 20)));
            Assert.That(tree.GetTooltip(secondArea.Center), Is.EqualTo("Second row"));

            context.Update(Time, Mouse(secondArea.Center.X, secondArea.Center.Y), new KeyboardState());
            context.Update(Time, Mouse(secondArea.Center.X, secondArea.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(secondArea.Center.X, secondArea.Center.Y), new KeyboardState());
            Assert.That(tree.SelectedItem, Is.SameAs(second));
        }

        [Test]
        public void TreeItem_MapsGodotPerCellCustomFontOverrides()
        {
            var tree = new Tree { Columns = 2 };
            var item = tree.CreateItem();
            Assert.That(item.GetCustomFont(0), Is.Null);
            Assert.That(item.GetCustomFontSize(0), Is.EqualTo(-1));

            item.SetCustomFontSize(0, 22);
            item.SetCustomFont(1, null);
            Assert.That(item.GetCustomFontSize(0), Is.EqualTo(22));
            Assert.That(item.GetCustomFont(1), Is.Null);

            item.SetCustomFontSize(0, -50);
            tree.Columns = 3;
            Assert.That(item.GetCustomFontSize(0), Is.EqualTo(-1));
            Assert.That(item.GetCustomFontSize(2), Is.EqualTo(-1));
        }

        [Test]
        public void TreeItem_MapsGodotPerCellCustomStyleBox()
        {
            var tree = new Tree { Columns = 2 };
            var item = tree.CreateItem();
            var style = new StyleBoxFlat { BackgroundColor = Color.CornflowerBlue, BorderColor = Color.White, BorderWidth = 1 };

            Assert.That(item.GetCustomStyleBox(0), Is.Null);
            item.SetCustomStyleBox(0, style);
            Assert.That(item.GetCustomStyleBox(0), Is.SameAs(style));
            item.SetCustomStyleBox(0, null);
            Assert.That(item.GetCustomStyleBox(0), Is.Null);
        }

        [Test]
        public void Tree_MapsGodotPerCellMultiSelectionAndModifierGestures()
        {
            var tree = new Tree { Size = new Vector2(160, 90), Columns = 2, SelectMode = TreeSelectMode.Multi, HideRoot = true };
            tree.CreateItem();
            var first = tree.CreateItem(); first.SetText(0, "First");
            var second = tree.CreateItem(); second.SetText(0, "Second");
            var third = tree.CreateItem(); third.SetText(0, "Third");
            var changes = new List<(TreeItem Item, bool Selected)>(); tree.MultiSelected += (_, item, _, selected) => changes.Add((item, selected));
            var context = new UIContext(); context.Add(tree); context.Layout();

            first.Select(); second.Select(setAsCursor: false);
            Assert.That(first.IsCellSelected(0), Is.True);
            Assert.That(second.IsCellSelected(0), Is.True);
            Assert.That(tree.SelectedItem, Is.SameAs(first));
            second.Deselect();
            Assert.That(second.IsAnyColumnSelected(), Is.False);

            var firstArea = tree.GetItemAreaRectangle(first); var secondArea = tree.GetItemAreaRectangle(second); var thirdArea = tree.GetItemAreaRectangle(third);
            var firstPoint = new Point(firstArea.X + 10, firstArea.Center.Y); var secondPoint = new Point(secondArea.X + 10, secondArea.Center.Y); var thirdPoint = new Point(thirdArea.X + 10, thirdArea.Center.Y);
            context.Update(Time, Mouse(firstPoint.X, firstPoint.Y), new KeyboardState());
            context.Update(Time, Mouse(firstPoint.X, firstPoint.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(firstPoint.X, firstPoint.Y), new KeyboardState());
            context.Update(Time, Mouse(secondPoint.X, secondPoint.Y), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(secondPoint.X, secondPoint.Y, ButtonState.Pressed), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(secondPoint.X, secondPoint.Y), new KeyboardState(Keys.LeftControl));
            Assert.That(first.IsCellSelected(0), Is.True);
            Assert.That(second.IsCellSelected(0), Is.True);

            context.Update(Time, Mouse(thirdPoint.X, thirdPoint.Y), new KeyboardState(Keys.LeftShift));
            context.Update(Time, Mouse(thirdPoint.X, thirdPoint.Y, ButtonState.Pressed), new KeyboardState(Keys.LeftShift));
            context.Update(Time, Mouse(thirdPoint.X, thirdPoint.Y), new KeyboardState(Keys.LeftShift));
            Assert.That(first.IsCellSelected(0), Is.False);
            Assert.That(second.IsCellSelected(0), Is.True);
            Assert.That(third.IsCellSelected(0), Is.True);
            Assert.That(tree.SelectedItem, Is.SameAs(third));
            Assert.That(changes, Is.Not.Empty);

            tree.DeselectAll();
            Assert.That(tree.IsAnythingSelected(), Is.False);
            Assert.That(third.IsAnyColumnSelected(), Is.False);

            tree.SetSelectMode(TreeSelectMode.Row); tree.SetSelected(second, 1);
            Assert.That(tree.GetSelectMode(), Is.EqualTo(TreeSelectMode.Row));
            Assert.That(second.IsCellSelected(0), Is.True);
            Assert.That(second.IsCellSelected(1), Is.True);
        }

        [Test]
        public void Tree_MapsGodotOptInRightMouseSelection()
        {
            var tree = new Tree { Size = new Vector2(160, 90), SelectMode = TreeSelectMode.Multi, AllowRightMouseSelect = true };
            var first = tree.CreateItem(); first.Text = "First";
            var second = tree.CreateItem(); second.Text = "Second";
            var third = tree.CreateItem(); third.Text = "Third";
            var mouseSignals = new List<(Point Position, PointerButton Button)>();
            tree.ItemMouseSelected += (_, position, button) => mouseSignals.Add((position, button));
            var context = new UIContext(); context.Add(tree); context.Layout();
            first.Select(); second.Select(setAsCursor: false);
            var firstArea = tree.GetItemAreaRectangle(first); var thirdArea = tree.GetItemAreaRectangle(third);
            var firstPoint = new Point(firstArea.X + 10, firstArea.Center.Y); var thirdPoint = new Point(thirdArea.X + 10, thirdArea.Center.Y);

            context.Update(Time, Mouse(firstPoint.X, firstPoint.Y), new KeyboardState());
            context.Update(Time, Mouse(firstPoint.X, firstPoint.Y, right: ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(firstPoint.X, firstPoint.Y), new KeyboardState());
            Assert.That(first.IsCellSelected(0), Is.True, "Right-clicking an existing multi-selection preserves it.");
            Assert.That(second.IsCellSelected(0), Is.True);
            Assert.That(mouseSignals, Has.Count.EqualTo(1));
            Assert.That(mouseSignals[0], Is.EqualTo((firstPoint, PointerButton.Right)));

            context.Update(Time, Mouse(thirdPoint.X, thirdPoint.Y, right: ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(thirdPoint.X, thirdPoint.Y), new KeyboardState());
            Assert.That(first.IsCellSelected(0), Is.False);
            Assert.That(second.IsCellSelected(0), Is.False);
            Assert.That(third.IsCellSelected(0), Is.True, "Right-clicking an unselected item replaces the multi-selection.");
            Assert.That(mouseSignals[1], Is.EqualTo((thirdPoint, PointerButton.Right)));

            var disabledTree = new Tree { Size = new Vector2(100, 40) };
            var disabledItem = disabledTree.CreateItem(); disabledItem.Text = "Disabled";
            var disabledContext = new UIContext(); disabledContext.Add(disabledTree); disabledContext.Layout();
            var disabledArea = disabledTree.GetItemAreaRectangle(disabledItem);
            disabledContext.Update(Time, Mouse(disabledArea.X + 4, disabledArea.Center.Y, right: ButtonState.Pressed), new KeyboardState());
            Assert.That(disabledTree.IsAnythingSelected(), Is.False, "allow_rmb_select defaults to false.");
        }

        [Test]
        public void Tree_MapsGodotKeyboardShiftRangeSelectionAnchor()
        {
            var tree = new Tree { Size = new Vector2(160, 100), SelectMode = TreeSelectMode.Multi };
            var first = tree.CreateItem(); first.Text = "First";
            var second = tree.CreateItem(); second.Text = "Second";
            var third = tree.CreateItem(); third.Text = "Third";
            var fourth = tree.CreateItem(); fourth.Text = "Fourth";
            var changes = new List<string>();
            tree.MultiSelected += (_, item, _, selected) => changes.Add(item.Text + ":" + selected);
            tree.Select(first);
            var context = new UIContext(); context.Add(tree); context.SetFocus(tree);

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Down));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Down));
            Assert.That(first.IsCellSelected(0), Is.True);
            Assert.That(second.IsCellSelected(0), Is.True);
            Assert.That(third.IsCellSelected(0), Is.True);
            Assert.That(fourth.IsCellSelected(0), Is.False);

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Down));
            Assert.That(first.IsCellSelected(0), Is.False);
            Assert.That(second.IsCellSelected(0), Is.False);
            Assert.That(third.IsCellSelected(0), Is.True);
            Assert.That(fourth.IsCellSelected(0), Is.True);
            Assert.That(changes, Does.Contain("First:False"));
            Assert.That(changes, Does.Contain("Fourth:True"));
        }

        [Test]
        public void TreeItem_MapsGodotPerCellMetadataAndTreeSearch()
        {
            var tree = new Tree { Columns = 2 };
            var root = tree.CreateItem(); var camera = root.CreateChild(); var hidden = root.CreateChild();
            root.SetMetadata(0, "root"); camera.SetMetadata(1, "node/camera"); hidden.SetMetadata(0, "node/hidden"); hidden.SetVisible(false);
            camera.Metadata = "node/camera-primary";

            Assert.That(camera.GetMetadata(0), Is.EqualTo("node/camera-primary"));
            Assert.That(camera.GetMetadata(1), Is.EqualTo("node/camera"));
            Assert.That(tree.GetItemWithMetadata("root"), Is.SameAs(root));
            Assert.That(tree.GetItemWithMetadata("node/camera", 1), Is.SameAs(camera));
            Assert.That(tree.GetItemWithMetadata("node/hidden"), Is.SameAs(hidden));
            Assert.That(tree.GetItemWithMetadata("missing"), Is.Null);
        }

        [Test]
        public void Tree_MapsGodotTextLookupAndFocusedIncrementalSearch()
        {
            var tree = new Tree { Columns = 2, IncrementalSearchTimeout = TimeSpan.FromMilliseconds(100) };
            var root = tree.CreateItem(); root.SetText(0, "Main");
            var camera = root.CreateChild(); camera.SetText(0, "Camera2D"); camera.SetText(1, "Camera");
            var camera3D = root.CreateChild(); camera3D.SetText(0, "Camera3D");
            var audio = root.CreateChild(); audio.SetText(0, "AudioStreamPlayer");
            var hidden = root.CreateChild(); hidden.SetText(0, "CameraHidden"); hidden.SetVisible(false);
            var context = new UIContext(); context.Add(tree); context.Update(Time, Mouse(0, 0), new KeyboardState());

            tree.Select(root);
            Assert.That(tree.GetItemWithText("Camera"), Is.SameAs(camera));
            Assert.That(tree.GetItemWithText("CameraHidden"), Is.Null);
            Assert.That(tree.SearchItemText("camera", out var column, selectable: true), Is.SameAs(camera));
            Assert.That(column, Is.EqualTo(0));
            tree.GrabFocus(); context.TextInput('c'); context.TextInput('a');
            Assert.That(tree.IncrementalSearchText, Is.EqualTo("ca"));
            Assert.That(tree.SelectedItem, Is.SameAs(camera3D));
            context.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), Mouse(0, 0), new KeyboardState());
            context.TextInput('a');
            Assert.That(tree.IncrementalSearchText, Is.EqualTo("a"));
            Assert.That(tree.SelectedItem, Is.SameAs(audio));
            tree.AllowSearch = false; tree.ClearIncrementalSearch(); context.TextInput('c');
            Assert.That(tree.IncrementalSearchText, Is.Empty);

            var lineEdit = new LineEdit(); context.Add(lineEdit); lineEdit.GrabFocus(); context.TextInput('X');
            Assert.That(lineEdit.Text, Is.EqualTo("X"));
        }

        [Test]
        public void TreeItem_MapsGodotCustomCellDrawingAndButtonPresentation()
        {
            var tree = new Tree { Size = new Vector2(120, 30) };
            var item = tree.CreateItem(); item.SetCellMode(0, TreeCellMode.Custom); item.SetEditable(0, true); item.SetCustomAsButton(0, true);
            var callback = new TreeItemCustomDrawCallback((_, drawnItem, cell) => { Assert.That(drawnItem, Is.SameAs(item)); Assert.That(cell.Width, Is.GreaterThan(0)); });
            item.SetCustomDrawCallback(0, callback);
            var edits = 0; tree.ItemEdited += (_, _, _) => edits++;
            var context = new UIContext(); context.Add(tree); context.Layout();

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());

            Assert.That(item.GetCustomDrawCallback(0), Is.SameAs(callback));
            Assert.That(item.IsCustomSetAsButton(0), Is.True);
            Assert.That(edits, Is.EqualTo(1));
        }

        [Test]
        public void ItemList_TracksPerItemStateMultiSelectionAndTilePositions()
        {
            var list = new ItemList { Size = new Vector2(200, 80), MaxColumns = 2, SelectionMode = ItemListSelectionMode.Multi, AutoHeight = true };
            var player = list.AddItem("Player");
            var enemy = list.AddItem("Enemy");
            var pickup = list.AddItem("Pickup");
            list.SetItemMetadata(player, "res://player.tscn");
            list.SetItemTooltip(player, "Player scene");
            list.SetItemDisabled(enemy, true);
            var changed = 0; list.MultiSelected += (_, _, _) => changed++;
            var context = new UIContext(); context.Add(list);

            // Godot's ItemList::gui_input makes a plain click EXCLUSIVE even in Multi mode (matching the
            // standard desktop multi-select convention) - only a held Ctrl/Cmd makes a click additive, so
            // the third click must hold Ctrl to keep Player selected alongside Pickup.
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(110, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(110, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 34, ButtonState.Pressed), new KeyboardState(Keys.LeftControl));

            Assert.That(list.GetItemMetadata(player), Is.EqualTo("res://player.tscn"));
            Assert.That(list.GetTooltip(new Point(10, 10)), Is.EqualTo("Player scene"));
            list.SetItemIconTransposed(player, true); list.SetItemIconRegion(player, new Rectangle(1, 2, 3, 4)); list.SetItemIconModulate(player, Color.CornflowerBlue); list.SetItemTagIcon(pickup, null);
            Assert.That(list.IsItemIconTransposed(player), Is.True);
            Assert.That(list.GetItemIconRegion(player), Is.EqualTo(new Rectangle(1, 2, 3, 4)));
            Assert.That(list.GetItemIconModulate(player), Is.EqualTo(Color.CornflowerBlue));
            Assert.That(list.GetItemTagIcon(pickup), Is.Null);
            Assert.That(list.IsSelected(player), Is.True);
            Assert.That(list.IsSelected(enemy), Is.False);
            Assert.That(list.IsSelected(pickup), Is.True);
            Assert.That(list.GetItemRect(pickup), Is.EqualTo(new Rectangle(0, 24, 100, 24)));
            Assert.That(list.GetMinimumSize().Y, Is.GreaterThanOrEqualTo(48));
            Assert.That(changed, Is.EqualTo(2));
        }

        [Test]
        public void ItemList_MapsGodotShiftArrowRangeSelectionAndAnchorResetOnShiftRelease()
        {
            var list = new ItemList { Size = new Vector2(100, 200), SelectionMode = ItemListSelectionMode.Multi, ItemHeight = 20 };
            for (var i = 0; i < 5; i++) list.AddItem($"Item {i}");
            var context = new UIContext(); context.Add(list); context.Layout();
            list.GrabFocus();
            list.Select(1);

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Down));
            Assert.That(list.GetSelectedItems(), Is.EqualTo(new[] { 1, 2 }), "Shift+Down extends the range from the anchor to the new current item.");
            Assert.That(list.Current, Is.EqualTo(2));

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Down));
            Assert.That(list.GetSelectedItems(), Is.EqualTo(new[] { 1, 2, 3 }), "Continuing to extend while shift stays held keeps the same anchor.");

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Up));
            Assert.That(list.GetSelectedItems(), Is.EqualTo(new[] { 1, 2 }), "Shrinking the range back toward the anchor deselects items that fall outside it.");
            Assert.That(list.Current, Is.EqualTo(2));

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Down));
            Assert.That(list.GetSelectedItems(), Is.EqualTo(new[] { 2, 3 }), "Godot resets the shift anchor when the Shift key itself is released, starting a fresh range from the current item.");
        }

        [Test]
        public void ItemList_MapsGodotSearchRightClickReselectAndCurrentReveal()
        {
            var list = new ItemList { Size = new Vector2(100, 48), ItemHeight = 24, SelectionMode = ItemListSelectionMode.Single, AllowReselect = true };
            list.AddItem("Alpha");
            list.AddItem("Beta");
            list.AddItem("Camera");
            list.AddItem("Canvas");
            list.AddItem("Delta");
            list.SetItemDisabled(3, true);
            var selected = new List<int>();
            list.ItemSelected += (_, index) => selected.Add(index);
            var context = new UIContext(); context.Add(list); list.GrabFocus();

            context.TextInput('c');

            Assert.That(list.Current, Is.EqualTo(2));
            Assert.That(list.GetIncrementalSearch(), Is.EqualTo("c"));
            Assert.That(list.ScrollOffsetY, Is.EqualTo(24));
            Assert.That(list.GetItemRect(2), Is.EqualTo(new Rectangle(0, 24, 99, 24)));

            context.Update(new GameTime(TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(1500)), Mouse(0, 0), new KeyboardState());
            context.TextInput('d');

            Assert.That(list.Current, Is.EqualTo(4), "Search timeout starts a new prefix instead of appending.");
            Assert.That(list.GetIncrementalSearch(), Is.EqualTo("d"));
            Assert.That(list.ScrollOffsetY, Is.EqualTo(72));

            list.SetAllowSearch(false);
            context.TextInput('a');
            Assert.That(list.Current, Is.EqualTo(4));
            Assert.That(list.GetIncrementalSearch(), Is.Empty);

            list.SetAllowRmbSelect(false);
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(10, 10, right: ButtonState.Pressed), new KeyboardState());
            Assert.That(list.Current, Is.EqualTo(4), "Right-click selection is opt-in in Godot.");

            list.SetAllowRmbSelect(true);
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, right: ButtonState.Pressed), new KeyboardState());
            Assert.That(list.IsSelected(3), Is.False, "Disabled entries become current only if selectable selection succeeds.");
            Assert.That(list.Current, Is.EqualTo(4));

            list.ScrollOffsetY = 0;
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, right: ButtonState.Pressed), new KeyboardState());
            Assert.That(list.Current, Is.EqualTo(0));

            list.Select(0);
            var zeroSelections = 0;
            foreach (var index in selected) if (index == 0) zeroSelections++;
            Assert.That(zeroSelections, Is.GreaterThanOrEqualTo(2), "allow_reselect re-emits item_selected for an already selected item.");

            list.CenterOnCurrent();
            Assert.That(list.ScrollOffsetY, Is.EqualTo(0));
            list.Select(4);
            list.CenterOnCurrent();
            Assert.That(list.ScrollOffsetY, Is.EqualTo(list.GetMaxScrollOffsetY()));
        }

        [Test]
        public void ItemList_MapsGodotAutoSizingScrollHintsAndWraparoundNavigation()
        {
            var list = new ItemList { Size = new Vector2(48, 48), ItemHeight = 24 };
            list.AddItem("One");
            list.AddItem("Two");
            list.AddItem("Three");

            Assert.That(list.HasAutoWidth(), Is.False);
            Assert.That(list.HasAutoHeight(), Is.False);
            // Godot's ItemList declares wraparound_items = true with no constructor override.
            Assert.That(list.HasWraparoundItems(), Is.True);
            Assert.That(list.GetScrollHintMode(), Is.EqualTo(ItemListScrollHintMode.Disabled));
            Assert.That(list.IsScrollHintTiled(), Is.False);

            list.SetAutoWidth(true);
            list.SetAutoHeight(true);
            list.SetScrollHintMode(ItemListScrollHintMode.Both);
            list.SetTileScrollHint(true);
            list.SetWraparoundItems(true);

            Assert.That(list.HasAutoWidth(), Is.True);
            Assert.That(list.HasAutoHeight(), Is.True);
            Assert.That(list.GetMinimumSize().X, Is.GreaterThanOrEqualTo(120));
            Assert.That(list.GetMinimumSize().Y, Is.GreaterThanOrEqualTo(72));
            Assert.That(list.GetScrollHintMode(), Is.EqualTo(ItemListScrollHintMode.Both));
            Assert.That(list.IsScrollHintTiled(), Is.True);
            Assert.That(list.HasWraparoundItems(), Is.True);

            var context = new UIContext(); context.Add(list); list.GrabFocus();
            list.Select(0);

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Left));
            Assert.That(list.Current, Is.EqualTo(2));

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Right));
            Assert.That(list.Current, Is.EqualTo(0));

            list.SetWraparoundItems(false);
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Left));
            Assert.That(list.Current, Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.SetScrollHintMode((ItemListScrollHintMode)99));
        }

        [Test]
        public void ItemList_MapsGodotPresentationTooltipAndEndPositionQueries()
        {
            var list = new ItemList { Size = new Vector2(120, 50), ItemHeight = 20, MaxColumns = 1, TooltipText = "List help" };
            list.AddItem("Alpha");
            list.AddItem("Beta");
            list.AddItem("Gamma");
            var context = new UIContext(); context.Add(list); context.Layout();

            Assert.That(list.GetItemTextDirection(0), Is.EqualTo(TextDirection.Inherited));
            Assert.That(list.GetItemLanguage(0), Is.Empty);
            Assert.That(list.GetItemAutoTranslateMode(0), Is.EqualTo(AutoTranslateMode.Inherit));
            Assert.That(list.GetTooltip(new Point(5, 5)), Is.EqualTo("Alpha"), "Godot falls back to item text when no explicit tooltip is set.");
            Assert.That(list.GetTooltipAutoTranslateModeAt(new Point(5, 5)), Is.EqualTo(AutoTranslateMode.Inherit));
            Assert.That(list.IsPosAtEndOfItems(new Point(5, 45)), Is.False);
            Assert.That(list.IsPosAtEndOfItems(new Point(5, 61)), Is.True);

            list.SetItemTooltip(1, "Translated beta");
            list.SetItemTextDirection(1, TextDirection.RightToLeft);
            list.SetItemLanguage(1, "ar");
            list.SetItemAutoTranslateMode(1, AutoTranslateMode.Disabled);
            Assert.That(list.GetItemTextDirection(1), Is.EqualTo(TextDirection.RightToLeft));
            Assert.That(list.GetItemLanguage(1), Is.EqualTo("ar"));
            Assert.That(list.GetItemAutoTranslateMode(1), Is.EqualTo(AutoTranslateMode.Disabled));
            Assert.That(list.GetTooltip(new Point(5, 25)), Is.EqualTo("Translated beta"));
            Assert.That(list.GetTooltipAutoTranslateModeAt(new Point(5, 25)), Is.EqualTo(AutoTranslateMode.Disabled));

            list.SetItemTooltipEnabled(1, false);
            Assert.That(list.GetTooltip(new Point(5, 25)), Is.Empty);
            list.SetItemLanguage(1, null);
            list.SetItemAutoTranslateMode(1, AutoTranslateMode.Always);
            Assert.That(list.GetItemLanguage(1), Is.Empty);
            Assert.That(list.GetItemAutoTranslateMode(1), Is.EqualTo(AutoTranslateMode.Always));
            Assert.That(list.GetTooltipAutoTranslateModeAt(new Point(200, 200)), Is.EqualTo(AutoTranslateMode.Inherit));

            list.ScrollOffsetY = 15;
            Assert.That(list.ScrollOffsetY, Is.EqualTo(10));
            Assert.That(list.IsPosAtEndOfItems(new Point(5, 49)), Is.False);
            Assert.That(list.IsPosAtEndOfItems(new Point(5, 51)), Is.True);
            var empty = new ItemList { Size = new Vector2(80, 20) };
            Assert.That(empty.IsPosAtEndOfItems(new Point(0, 0)), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => list.SetItemTextDirection(0, (TextDirection)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.SetItemAutoTranslateMode(0, (AutoTranslateMode)99));
        }

        [Test]
        public void ItemList_ToggleModeAndCtrlClickDeselectMatchGodotsClickBranching()
        {
            var list = new ItemList { Size = new Vector2(100, 100), ItemHeight = 20, SelectionMode = ItemListSelectionMode.Toggle };
            list.AddItem("A"); list.AddItem("B");
            var context = new UIContext(); context.Add(list);

            // Godot's SELECT_TOGGLE click branch selects an unselected item...
            context.Update(Time, Mouse(5, 5), new KeyboardState());
            context.Update(Time, Mouse(5, 5, ButtonState.Pressed), new KeyboardState());
            Assert.That(list.IsSelected(0), Is.True);
            context.Update(Time, Mouse(5, 5), new KeyboardState());

            // ...and deselects an already-selected one on a second click of the SAME item.
            context.Update(Time, Mouse(5, 5, ButtonState.Pressed), new KeyboardState());
            Assert.That(list.IsSelected(0), Is.False, "Toggle mode must be able to select AND deselect via plain clicks.");
        }

        [Test]
        public void ItemList_CtrlClickDeselectsAndPlainClickIsExclusiveInMultiMode()
        {
            var list = new ItemList { Size = new Vector2(100, 100), ItemHeight = 20, SelectionMode = ItemListSelectionMode.Multi };
            list.AddItem("A"); list.AddItem("B");
            var context = new UIContext(); context.Add(list);

            context.Update(Time, Mouse(5, 5), new KeyboardState());
            context.Update(Time, Mouse(5, 5, ButtonState.Pressed), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(5, 5), new KeyboardState());
            context.Update(Time, Mouse(5, 25, ButtonState.Pressed), new KeyboardState(Keys.LeftControl));
            Assert.That(list.GetSelectedItems(), Is.EqualTo(new[] { 0, 1 }), "Ctrl-click on an unselected item ADDS to the selection.");

            context.Update(Time, Mouse(5, 25), new KeyboardState());
            context.Update(Time, Mouse(5, 25, ButtonState.Pressed), new KeyboardState(Keys.LeftControl));
            Assert.That(list.GetSelectedItems(), Is.EqualTo(new[] { 0 }), "Godot's Ctrl-click on an already-selected Multi-mode item deselects just that item.");

            context.Update(Time, Mouse(5, 5), new KeyboardState());
            context.Update(Time, Mouse(5, 25, ButtonState.Pressed), new KeyboardState());
            Assert.That(list.GetSelectedItems(), Is.EqualTo(new[] { 1 }), "A plain click without Ctrl is exclusive, even in Multi mode.");
        }

        [Test]
        public void ItemList_MultiModeKeyboardNavigationMovesCursorWithoutSelectingLikeGodotSetCurrent()
        {
            // Godot's set_current only actually selects in Single mode; in Multi/Toggle mode arrow-key
            // navigation and incremental search are pure keyboard-cursor bookkeeping.
            var list = new ItemList { Size = new Vector2(100, 100), ItemHeight = 20, SelectionMode = ItemListSelectionMode.Multi };
            list.AddItem("A"); list.AddItem("B"); list.AddItem("C");
            var context = new UIContext(); context.Add(list); list.GrabFocus();
            var selected = 0; list.ItemSelected += (_, _) => selected++;

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Down));
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Down));

            Assert.That(list.Current, Is.EqualTo(2), "The keyboard cursor still moves.");
            Assert.That(list.GetSelectedItems(), Is.Empty, "But nothing gets selected.");
            Assert.That(selected, Is.EqualTo(0), "ItemSelected never fires outside Single mode.");
        }

        [Test]
        public void ItemList_DeselectOnlyClearsCurrentInSingleModeAndRemoveItemDoesNotShiftCurrent()
        {
            var multi = new ItemList { SelectionMode = ItemListSelectionMode.Multi };
            multi.AddItem("A"); multi.AddItem("B"); multi.AddItem("C");
            // single=true still sets `current` regardless of mode, matching Godot's select() - only a
            // Multi-mode Ctrl-click ADD (single=false) deliberately leaves `current` untouched.
            multi.Select(2, true);
            multi.Deselect(2);
            // Godot's deselect() only resets `current` in Single mode; Multi/Toggle mode leaves the
            // keyboard cursor where it was.
            Assert.That(multi.Current, Is.EqualTo(2), "Deselecting in Multi mode must not clear the keyboard cursor.");

            var list = new ItemList();
            list.AddItem("A"); list.AddItem("B"); list.AddItem("C");
            list.Select(2, false);
            list.RemoveItem(0);
            // Godot's remove_item does NOT shift `current` down for later indices - a real (if arguably
            // buggy) Godot quirk matched here for behavioral parity, same family as OptionButton's
            // remove_item.
            Assert.That(list.Current, Is.EqualTo(2), "Current must not shift down after removing an earlier item.");
        }

        [Test]
        public void ItemList_ItemActivatedOnlyFiresOnDoubleClickLikeGodot()
        {
            var list = new ItemList { Size = new Vector2(100, 100), ItemHeight = 20 };
            list.AddItem("A"); list.AddItem("B");
            var activated = new List<int>();
            list.ItemActivated += (_, index) => activated.Add(index);
            var context = new UIContext(); context.Add(list);

            context.Update(Time, Mouse(5, 5), new KeyboardState());
            context.Update(Time, Mouse(5, 5, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(5, 5), new KeyboardState());
            Assert.That(activated, Is.Empty, "A single click must not fire ItemActivated, matching Godot's item_activated only firing on a genuine double-click.");

            context.Update(Time, Mouse(5, 5, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(5, 5), new KeyboardState());
            Assert.That(activated, Is.EqualTo(new[] { 0 }), "A second click on the same item within the double-click window fires ItemActivated.");
        }

        [Test]
        public void ItemList_EnterActivatesOnlyWhenCurrentItemIsNotDisabledLikeGodot()
        {
            var list = new ItemList { Size = new Vector2(100, 100), ItemHeight = 20 };
            list.AddItem("A"); list.AddItem("B");
            list.SetCurrent(0);
            list.SetItemDisabled(0, true);
            var activated = new List<int>();
            list.ItemActivated += (_, index) => activated.Add(index);
            var context = new UIContext(); context.Add(list); list.GrabFocus();

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Enter));

            Assert.That(activated, Is.Empty, "Enter must not activate a disabled current item, matching Godot's ui_accept guard.");
        }

        [Test]
        public void ItemList_SelectWithSingleFalseDoesNotClobberCurrentLikeGodot()
        {
            var list = new ItemList { SelectionMode = ItemListSelectionMode.Multi };
            list.AddItem("A"); list.AddItem("B"); list.AddItem("C");
            list.SetCurrent(0);

            list.Select(2, false);

            Assert.That(list.Current, Is.EqualTo(0), "A Multi-mode Ctrl-click-style add (single=false) must not move the keyboard cursor, matching Godot's select().");
            Assert.That(list.IsSelected(2), Is.True);
        }

        [Test]
        public void ItemList_PageUpAndPageDownJumpFourRowsLikeGodot()
        {
            var list = new ItemList { Size = new Vector2(100, 200), ItemHeight = 20 };
            for (var i = 0; i < 10; i++) list.AddItem("Item" + i);
            list.SetCurrent(0);
            var context = new UIContext(); context.Add(list); list.GrabFocus();

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.PageDown));
            Assert.That(list.Current, Is.EqualTo(4), "PageDown should jump 4 rows (columns=1) forward, matching Godot's ui_page_down.");

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.PageUp));
            Assert.That(list.Current, Is.EqualTo(0), "PageUp should jump back 4 rows, matching Godot's ui_page_up.");
        }

        [Test]
        public void ItemList_SpaceTogglesCurrentSelectionWithoutMovingCursorLikeGodotsUiSelect()
        {
            var list = new ItemList { Size = new Vector2(100, 100), ItemHeight = 20, SelectionMode = ItemListSelectionMode.Multi };
            list.AddItem("A"); list.AddItem("B");
            list.SetCurrent(0);
            var context = new UIContext(); context.Add(list); list.GrabFocus();

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Space));
            Assert.That(list.IsSelected(0), Is.True);
            Assert.That(list.Current, Is.EqualTo(0));

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Space));
            Assert.That(list.IsSelected(0), Is.False, "A second Space toggles the selection back off.");
        }

        [Test]
        public void ItemList_ShiftRangeSelectSkipsDisabledItemsStateButStillFiresSignalLikeGodot()
        {
            var list = new ItemList { Size = new Vector2(100, 100), ItemHeight = 20, SelectionMode = ItemListSelectionMode.Multi };
            list.AddItem("A"); list.AddItem("B"); list.AddItem("C");
            list.SetItemDisabled(1, true);
            list.SetCurrent(0);
            var multiSelected = new List<(int index, bool selected)>();
            list.MultiSelected += (_, index, selected) => multiSelected.Add((index, selected));
            var context = new UIContext(); context.Add(list); list.GrabFocus();

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Down));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.Down));

            Assert.That(list.IsSelected(1), Is.False, "A disabled item's selection state never actually flips, matching Godot's select() no-op.");
            Assert.That(list.IsSelected(2), Is.True);
            Assert.That(multiSelected.Exists(e => e.index == 1 && e.selected), Is.True, "MultiSelected still fires unconditionally for it though, matching Godot's real _shift_range_select quirk.");
        }

        [Test]
        public void ItemList_SortItemsByTextReAnchorsCurrentToTheSelectedItemLikeGodot()
        {
            var list = new ItemList();
            list.AddItem("Charlie"); list.AddItem("Alpha"); list.AddItem("Bravo");
            list.Select(0);

            list.SortItemsByText();

            Assert.That(list.GetItemText(0), Is.EqualTo("Alpha"));
            Assert.That(list.Current, Is.EqualTo(2), "Current must follow the selected item to its new sorted position, matching Godot's sort_items_by_text re-anchor.");
            Assert.That(list.IsSelected(2), Is.True);
        }

        [Test]
        public void TabBar_TracksTabStateAndEmitsCloseWithoutSelectingDisabledTabs()
        {
            var tabs = new TabBar { Size = new Vector2(180, 28), TabSizing = TabBarSizingMode.Justify, CloseDisplayPolicy = TabCloseDisplayPolicy.Always };
            tabs.AddTab("Scene"); tabs.AddTab("Script"); tabs.AddTab("Asset");
            tabs.SetTabMetadata(0, "scene"); tabs.SetTabTooltip(0, "Scene dock"); tabs.SetTabDisabled(1, true); tabs.SetTabHidden(2, true);
            var closed = -1; tabs.TabClosePressed += (_, index) => closed = index;
            var context = new UIContext(); context.Add(tabs);

            context.Update(Time, Mouse(100, 10), new KeyboardState());
            context.Update(Time, Mouse(100, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(80, 10), new KeyboardState());
            context.Update(Time, Mouse(80, 10, ButtonState.Pressed), new KeyboardState());

            Assert.That(tabs.CurrentTab, Is.EqualTo(0));
            Assert.That(tabs.GetTabMetadata(0), Is.EqualTo("scene"));
            Assert.That(tabs.GetTooltip(new Point(10, 10)), Is.EqualTo("Scene dock"));
            Assert.That(closed, Is.EqualTo(0));
        }

        [Test]
        public void TabBar_DefaultTemplateMinimumSizeDoesNotReenterOwnerMeasurement()
        {
            var tabs = new TabBar { CustomMinimumSize = new Vector2(120, 28) };
            tabs.AddTab("Scene");
            tabs.ApplyTemplate();

            Assert.That(tabs.GetMinimumSize(), Is.EqualTo(new Vector2(120, 28)));
        }

        [Test]
        public void TabBar_RemoveTabShiftsCurrentAndPreviousIndicesLikeGodot()
        {
            var tabs = new TabBar();
            tabs.AddTab("A"); tabs.AddTab("B"); tabs.AddTab("C"); tabs.AddTab("D");
            tabs.CurrentTab = 2;

            tabs.RemoveTab(0);

            Assert.That(tabs.CurrentTab, Is.EqualTo(1), "Godot's remove_tab decrements current when the removed index is at or before it, keeping the same logical tab selected.");
            Assert.That(tabs.GetTabTitle(tabs.CurrentTab), Is.EqualTo("C"));
        }

        [Test]
        public void TabBar_DisablingOrHidingTheActiveTabDoesNotForceReselectionLikeGodot()
        {
            var tabs = new TabBar();
            tabs.AddTab("A"); tabs.AddTab("B");
            tabs.CurrentTab = 0;

            tabs.SetTabDisabled(0, true);
            Assert.That(tabs.CurrentTab, Is.EqualTo(0), "Godot's set_tab_disabled never touches `current`.");

            tabs.SetTabHidden(0, true);
            Assert.That(tabs.CurrentTab, Is.EqualTo(0), "Godot's set_tab_hidden never touches `current` either.");
        }

        [Test]
        public void TabBar_CurrentTabSetterAllowsSelectingADisabledOrHiddenTabLikeGodot()
        {
            var tabs = new TabBar();
            tabs.AddTab("A"); tabs.AddTab("B");
            tabs.SetTabDisabled(1, true);

            tabs.CurrentTab = 1;

            Assert.That(tabs.CurrentTab, Is.EqualTo(1), "Godot's set_current_tab has no disabled/hidden guard; only the mouse-click path blocks it.");
        }

        [Test]
        public void TabBar_TracksPreviousTabLikeGodot()
        {
            var tabs = new TabBar();
            tabs.AddTab("A"); tabs.AddTab("B"); tabs.AddTab("C");

            Assert.That(tabs.GetPreviousTab(), Is.EqualTo(-1));
            tabs.CurrentTab = 2;
            Assert.That(tabs.GetPreviousTab(), Is.EqualTo(0), "AddTab's auto-select of the first tab leaves previous at -1, so switching away from it reports 0.");
            tabs.CurrentTab = 1;
            Assert.That(tabs.GetPreviousTab(), Is.EqualTo(2));
        }

        [Test]
        public void TabBar_AddTabOnlyAutoSelectsTheVeryFirstTabWhenDeselectionIsDisabledLikeGodot()
        {
            var suppressed = new TabBar { DeselectEnabled = true };
            suppressed.AddTab("A");
            Assert.That(suppressed.CurrentTab, Is.EqualTo(-1), "DeselectEnabled must suppress AddTab's auto-select of the first tab.");

            var tabs = new TabBar { DeselectEnabled = true };
            tabs.AddTab("A"); tabs.AddTab("B");
            tabs.CurrentTab = -1;

            tabs.AddTab("C");

            Assert.That(tabs.CurrentTab, Is.EqualTo(-1), "AddTab must never re-auto-select on the 2nd+ tab, even when nothing is currently selected.");
        }

        [Test]
        public void TabBar_MapsGodotPerTabButtonIconAndSignalSeparatelyFromClose()
        {
            var tabs = new TabBar { Size = new Vector2(180, 28), TabSizing = TabBarSizingMode.Justify, CloseDisplayPolicy = TabCloseDisplayPolicy.Always };
            var buttonIcon = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            tabs.AddTab("Scene");
            tabs.AddTab("Script");
            tabs.SetTabButtonIcon(0, buttonIcon);
            var buttonPressed = -1;
            var closed = -1;
            tabs.TabButtonPressed += (_, index) => buttonPressed = index;
            tabs.TabClosePressed += (_, index) => closed = index;
            var context = new UIContext(); context.Add(tabs);

            Assert.That(tabs.GetTabButtonIcon(0), Is.SameAs(buttonIcon));
            Assert.That(tabs.GetTabButtonIcon(1), Is.Null);
            Assert.That(tabs.GetTabRect(0).Width, Is.GreaterThan(60), "The retained right-button contributes to tab width.");

            context.Update(Time, Mouse(65, 10), new KeyboardState());
            context.Update(Time, Mouse(65, 10, ButtonState.Pressed), new KeyboardState());

            Assert.That(buttonPressed, Is.EqualTo(0));
            Assert.That(closed, Is.EqualTo(-1));

            context.Update(Time, Mouse(80, 10), new KeyboardState());
            context.Update(Time, Mouse(80, 10, ButtonState.Pressed), new KeyboardState());

            Assert.That(closed, Is.EqualTo(0));
            Assert.That(buttonPressed, Is.EqualTo(0));
        }

        [Test]
        public void TabBar_MapsGodotSizingAlignmentAndMaximumTabWidth()
        {
            var tabs = new TabBar { Size = new Vector2(300, 28) };
            tabs.SetTabSizing(TabBarSizingMode.FitContent);
            tabs.SetTabAlignment(TabBarAlignment.Right);
            tabs.SetMaxTabWidth(70);
            tabs.AddTab("Scene"); tabs.AddTab("Long inspector title"); tabs.AddTab("Log");
            tabs.SetTabTextDirection(1, TextDirection.RightToLeft);
            tabs.SetTabLanguage(1, "ar");
            var scene = tabs.GetTabRect(0); var inspector = tabs.GetTabRect(1); var log = tabs.GetTabRect(2);
            tabs.SetTabSizing(TabBarSizingMode.Uniform);

            Assert.That(scene.X, Is.GreaterThan(0));
            Assert.That(inspector.Width, Is.EqualTo(70));
            Assert.That(log.Right, Is.EqualTo(300));
            Assert.That(tabs.GetTabCount(), Is.EqualTo(3));
            Assert.That(tabs.GetTabAlignment(), Is.EqualTo(TabBarAlignment.Right));
            Assert.That(tabs.GetTabSizing(), Is.EqualTo(TabBarSizingMode.Uniform));
            Assert.That(tabs.GetMaxTabWidth(), Is.EqualTo(70));
            Assert.That(tabs.GetTabTextDirection(1), Is.EqualTo(TextDirection.RightToLeft));
            Assert.That(tabs.GetTabLanguage(1), Is.EqualTo("ar"));
            Assert.That(tabs.GetTabRect(0).Width, Is.EqualTo(tabs.GetTabRect(1).Width));
            tabs.SetTabCount(2);
            Assert.That(tabs.GetTabCount(), Is.EqualTo(2));
            tabs.SetTabCount(4);
            Assert.That(tabs.GetTabTitle(3), Is.EqualTo(string.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => tabs.SetTabCount(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => tabs.SetTabSizing((TabBarSizingMode)999));
            Assert.Throws<ArgumentOutOfRangeException>(() => tabs.SetTabTextDirection(0, (TextDirection)999));
        }

        [Test]
        public void TabBar_ScrollsOverflowAndKeepsTheCurrentTabVisible()
        {
            var tabs = new TabBar { Size = new Vector2(90, 28), TabSizing = TabBarSizingMode.FitContent, ScrollToSelected = true };
            tabs.AddTab("Scene"); tabs.AddTab("Inspector"); tabs.AddTab("Output");
            tabs.CurrentTab = 2;

            var output = tabs.GetTabRect(2);
            Assert.That(tabs.OffsetButtonsVisible, Is.True);
            Assert.That(tabs.TabOffset, Is.GreaterThan(0));
            Assert.That(output.Right, Is.GreaterThan(tabs.Bounds.Left));
            Assert.That(output.Left, Is.LessThan(tabs.Bounds.Right));

            tabs.TabOffset = 1;
            Assert.That(tabs.GetTabRect(1).Left, Is.GreaterThan(tabs.Bounds.Left));
            var context = new UIContext(); context.Add(tabs);
            context.Update(Time, Mouse(7, 10), new KeyboardState());
            context.Update(Time, Mouse(7, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(tabs.TabOffset, Is.EqualTo(0));
            tabs.TabOffset = 1;
            tabs.EnsureTabVisible(0);
            Assert.That(tabs.TabOffset, Is.EqualTo(0));
        }

        [Test]
        public void TabBar_NavigatesOnlyAvailableTabsUsingKeyboardDirection()
        {
            var tabs = new TabBar { Size = new Vector2(180, 28) };
            tabs.AddTab("Scene"); tabs.AddTab("Disabled"); tabs.AddTab("Inspector"); tabs.AddTab("Hidden");
            tabs.SetTabDisabled(1, true); tabs.SetTabHidden(3, true);

            tabs.KeyPressed(Keys.Right);
            Assert.That(tabs.CurrentTab, Is.EqualTo(2));
            Assert.That(tabs.GetPreviousAvailable(), Is.EqualTo(0));
            Assert.That(tabs.GetNextAvailable(), Is.EqualTo(-1));
            tabs.KeyPressed(Keys.Left);
            Assert.That(tabs.CurrentTab, Is.EqualTo(0));
            Assert.That(tabs.SelectPreviousAvailable(), Is.False);
            Assert.That(tabs.SelectNextAvailable(), Is.True);
            Assert.That(tabs.CurrentTab, Is.EqualTo(2));
        }

        [Test]
        public void TabBar_MapsGodotRightClickDeselectAndMiddleClosePolicies()
        {
            var tabs = new TabBar { Size = new Vector2(180, 28), TabSizing = TabBarSizingMode.Justify };
            tabs.AddTab("Scene"); tabs.AddTab("Inspector"); tabs.AddTab("Output");
            var clicked = new List<int>();
            var rightClicked = new List<int>();
            var selected = new List<int>();
            var changed = new List<int>();
            var closed = new List<int>();
            var hovered = new List<int>();
            tabs.TabClicked += (_, tab) => clicked.Add(tab);
            tabs.TabRightClicked += (_, tab) => rightClicked.Add(tab);
            tabs.TabSelected += (_, tab) => selected.Add(tab);
            tabs.TabChanged += (_, tab) => changed.Add(tab);
            tabs.TabClosePressed += (_, tab) => closed.Add(tab);
            tabs.TabHovered += (_, tab) => hovered.Add(tab);
            var context = new UIContext(); context.Add(tabs);

            Assert.That(tabs.GetSelectWithRmb(), Is.False);
            Assert.That(tabs.GetDeselectEnabled(), Is.False);
            Assert.That(tabs.GetSwitchOnDragHover(), Is.True);
            tabs.SetSwitchOnDragHover(false);
            Assert.That(tabs.GetSwitchOnDragHover(), Is.False);
            Assert.Throws<InvalidOperationException>(() => tabs.CurrentTab = -1);

            var inspector = tabs.GetTabRect(1).Center;
            context.Update(Time, Mouse(inspector.X, inspector.Y), new KeyboardState());
            context.Update(Time, Mouse(inspector.X, inspector.Y, right: ButtonState.Pressed), new KeyboardState());

            Assert.That(tabs.CurrentTab, Is.EqualTo(0));
            Assert.That(rightClicked, Is.EqualTo(new[] { 1 }));
            Assert.That(clicked, Is.Empty, "Right-click does not select or emit tab_clicked until select_with_rmb is enabled.");
            Assert.That(hovered, Is.EqualTo(new[] { 1 }));

            tabs.SetSelectWithRmb(true);
            context.Update(Time, Mouse(inspector.X, inspector.Y), new KeyboardState());
            context.Update(Time, Mouse(inspector.X, inspector.Y, right: ButtonState.Pressed), new KeyboardState());

            Assert.That(tabs.CurrentTab, Is.EqualTo(1));
            Assert.That(rightClicked, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(clicked, Is.EqualTo(new[] { 1 }));
            Assert.That(selected, Is.EqualTo(new[] { 1 }));
            Assert.That(changed, Is.EqualTo(new[] { 1 }));

            tabs.SetDeselectEnabled(true);
            tabs.CurrentTab = -1;

            Assert.That(tabs.CurrentTab, Is.EqualTo(-1));
            Assert.That(selected[selected.Count - 1], Is.EqualTo(-1));
            Assert.That(changed[changed.Count - 1], Is.EqualTo(-1));
            Assert.DoesNotThrow(() => tabs.SetTabTitle(2, "Output"));
            Assert.DoesNotThrow(() => tabs.SetTabIcon(2, null));
            Assert.DoesNotThrow(() => tabs.SetTabHidden(2, false));
            Assert.DoesNotThrow(() => tabs.MoveTab(2, 2));

            tabs.SetDeselectEnabled(false);
            Assert.That(tabs.CurrentTab, Is.EqualTo(0), "Disabling deselect while no tab is selected restores the next available tab.");

            var output = tabs.GetTabRect(2).Center;
            context.Update(Time, Mouse(output.X, output.Y), new KeyboardState());
            context.Update(Time, Mouse(output.X, output.Y, middle: ButtonState.Pressed), new KeyboardState());
            Assert.That(closed, Is.EqualTo(new[] { 2 }));
            Assert.That(hovered, Is.EqualTo(new[] { 1, 2 }));

            tabs.SetCloseWithMiddleMouse(false);
            context.Update(Time, Mouse(output.X, output.Y), new KeyboardState());
            context.Update(Time, Mouse(output.X, output.Y, middle: ButtonState.Pressed), new KeyboardState());
            Assert.That(closed, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void ScrollContainer_RevealsFocusedDescendantsWithBorderAndExposesScrollPositions()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 80), FollowFocus = true };
            var canvas = new Panel { CustomMinimumSize = new Vector2(300, 260) };
            var target = new Button { Position = new Vector2(220, 180), Size = new Vector2(40, 24) };
            canvas.AddChild(target); scroll.AddChild(canvas);
            var context = new UIContext(); context.Add(scroll);

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            target.GrabFocus();
            context.Update(Time, Mouse(0, 0), new KeyboardState());

            Assert.That(scroll.HorizontalScroll, Is.GreaterThan(0));
            Assert.That(scroll.VerticalScroll, Is.GreaterThan(0));
            Assert.That(scroll.HorizontalScrollBar.Value, Is.EqualTo(scroll.HorizontalScroll));
            scroll.HorizontalCustomStep = 12; scroll.VerticalCustomStep = 18;
            // HorizontalCustomStep/VerticalCustomStep forward to the underlying scrollbars' own
            // CustomStep, matching Godot's set_horizontal_custom_step/set_vertical_custom_step.
            Assert.That(scroll.HorizontalScrollBar.CustomStep, Is.EqualTo(12));
            Assert.That(scroll.VerticalScrollBar.CustomStep, Is.EqualTo(18));
            scroll.SetShowHorizontalStepButtons(false);
            scroll.ShowVerticalStepButtons = false;
            Assert.That(scroll.IsShowingHorizontalStepButtons(), Is.False);
            Assert.That(scroll.IsShowingVerticalStepButtons(), Is.False);
            Assert.That(scroll.HorizontalScrollBar.ShowStepButtons, Is.False);
            Assert.That(scroll.VerticalScrollBar.ShowStepButtons, Is.False);
            scroll.ScrollTo(Vector2.Zero); scroll.EnsureControlVisible(target);
            Assert.That(scroll.ScrollOffset.X, Is.GreaterThan(0));
            Assert.That(scroll.ScrollOffset.Y, Is.GreaterThan(0));
        }

        [Test]
        public void ScrollContainer_MapsNestedFocusVisibilityAndOversizedTargets()
        {
            var outer = new ScrollContainer { Size = new Vector2(100, 100), FollowFocus = true, HorizontalScrollMode = ScrollBarVisibility.Disabled };
            var outerCanvas = new Panel { CustomMinimumSize = new Vector2(80, 300) };
            var inner = new ScrollContainer { Position = new Vector2(0, 180), Size = new Vector2(80, 100), FollowFocus = true, HorizontalScrollMode = ScrollBarVisibility.Disabled };
            var innerCanvas = new Panel { CustomMinimumSize = new Vector2(80, 300) };
            var target = new Button { Position = new Vector2(0, 220), Size = new Vector2(40, 24) };
            innerCanvas.AddChild(target); inner.AddChild(innerCanvas); outerCanvas.AddChild(inner); outer.AddChild(outerCanvas);
            var context = new UIContext(); context.Add(outer); context.Layout();

            target.GrabFocus(); context.Update(Time, Mouse(0, 0), new KeyboardState());

            Assert.That(inner.VerticalScroll, Is.EqualTo(144));
            Assert.That(outer.VerticalScroll, Is.EqualTo(180), "The outer container reveals only the target slice visible through the pending inner viewport scroll.");

            var oversized = new ScrollContainer { Size = new Vector2(100, 80), HorizontalScrollMode = ScrollBarVisibility.Disabled };
            var canvas = new Panel { CustomMinimumSize = new Vector2(80, 300) };
            var largeTarget = new Button { Position = new Vector2(0, 50), Size = new Vector2(40, 120) };
            canvas.AddChild(largeTarget); oversized.AddChild(canvas);
            var oversizedContext = new UIContext(); oversizedContext.Add(oversized); oversizedContext.Layout();

            oversized.EnsureControlVisible(largeTarget);
            Assert.That(oversized.VerticalScroll, Is.EqualTo(50), "A target larger than the viewport aligns its beginning when it starts beyond the visible area.");
        }

        [Test]
        public void ScrollContainer_MapsGodotPublicScrollStateAndGetterAliases()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 80) };
            var canvas = new Panel { CustomMinimumSize = new Vector2(300, 260) };
            var target = new Button { Position = new Vector2(220, 180), Size = new Vector2(40, 24) };
            canvas.AddChild(target); scroll.AddChild(canvas);
            var context = new UIContext(); context.Add(scroll); context.Layout();

            Assert.That(scroll.IsFollowingFocus(), Is.False);
            Assert.That(scroll.GetDeadzone(), Is.EqualTo(0));
            Assert.That(scroll.GetScrollHintMode(), Is.EqualTo(ScrollContainerScrollHintMode.Disabled));
            Assert.That(scroll.IsScrollHintTiled(), Is.False);
            Assert.That(scroll.GetDrawFocusBorder(), Is.False);
            Assert.That(scroll.IsScrollHorizontalByDefault(), Is.False);
            Assert.That(scroll.GetHScrollBar(), Is.SameAs(scroll.HorizontalScrollBar));
            Assert.That(scroll.GetVScrollBar(), Is.SameAs(scroll.VerticalScrollBar));

            scroll.SetFollowFocus(true);
            scroll.SetDeadzone(18);
            scroll.SetScrollHintMode(ScrollContainerScrollHintMode.TopAndLeft);
            scroll.SetTileScrollHint(true);
            scroll.SetDrawFocusBorder(true);
            scroll.SetScrollHorizontalByDefault(true);
            scroll.SetHorizontalCustomStep(12);
            scroll.SetVerticalCustomStep(20);
            scroll.SetHorizontalScrollMode(ScrollBarVisibility.Reserve);
            scroll.SetVerticalScrollMode(ScrollBarVisibility.Always);
            scroll.SetHScroll(70);
            scroll.SetVScroll(90);

            Assert.That(scroll.IsFollowingFocus(), Is.True);
            Assert.That(scroll.GetDeadzone(), Is.EqualTo(18));
            Assert.That(scroll.GetScrollHintMode(), Is.EqualTo(ScrollContainerScrollHintMode.TopAndLeft));
            Assert.That(scroll.IsScrollHintTiled(), Is.True);
            Assert.That(scroll.GetDrawFocusBorder(), Is.True);
            Assert.That(scroll.IsScrollHorizontalByDefault(), Is.True);
            Assert.That(scroll.GetHorizontalCustomStep(), Is.EqualTo(12));
            Assert.That(scroll.GetVerticalCustomStep(), Is.EqualTo(20));
            Assert.That(scroll.GetHorizontalScrollMode(), Is.EqualTo(ScrollBarVisibility.Reserve));
            Assert.That(scroll.GetVerticalScrollMode(), Is.EqualTo(ScrollBarVisibility.Always));
            Assert.That(scroll.GetHScroll(), Is.EqualTo(scroll.HorizontalScroll));
            Assert.That(scroll.GetVScroll(), Is.EqualTo(scroll.VerticalScroll));
            Assert.That(scroll.GetHScrollBar().Visible, Is.True);
            Assert.That(scroll.GetVScrollBar().Visible, Is.True);

            scroll.ScrollTo(Vector2.Zero);
            scroll.EnsureControlVisible(target);
            Assert.That(scroll.GetHScroll(), Is.GreaterThan(0));
            Assert.That(scroll.GetVScroll(), Is.GreaterThan(0));
            Assert.Throws<ArgumentNullException>(() => scroll.EnsureControlVisible(null));
            Assert.Throws<ArgumentException>(() => scroll.EnsureControlVisible(new Control()));
            Assert.Throws<ArgumentOutOfRangeException>(() => scroll.SetScrollHintMode((ScrollContainerScrollHintMode)99));
        }

        [Test]
        public void ScrollContainer_MapsGodotScrollHintAndFocusPanelPresentation()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 80), DrawFocusBorder = true };
            var content = new Panel { CustomMinimumSize = new Vector2(80, 240) };
            var child = new Button { Position = new Vector2(10, 120), Size = new Vector2(30, 20) };
            content.AddChild(child); scroll.AddChild(content);
            var context = new UIContext(); context.Add(scroll); context.Layout();
            scroll.SetScrollHintMode(ScrollContainerScrollHintMode.All);

            Assert.That(scroll.GetVisibleScrollHintRectangles(), Is.EqualTo(new[] { new Rectangle(0, 76, 100, 4) }), "At the top, only the bottom overflow hint is visible.");
            scroll.SetVScroll(80);
            Assert.That(scroll.GetVisibleScrollHintRectangles(), Is.EqualTo(new[] { new Rectangle(0, 0, 100, 4), new Rectangle(0, 76, 100, 4) }));
            child.GrabFocus();
            Assert.That(scroll.IsFocusBorderVisible, Is.True, "Godot draws the focus panel when a descendant owns focus.");

            content.CustomMinimumSize = new Vector2(240, 240); context.Layout();
            Assert.That(scroll.GetVisibleScrollHintRectangles(), Is.Empty, "Godot suppresses edge hints when both axes need hints.");
        }

        [Test]
        public void ScrollContainer_MapsGodotTouchDragDeadzoneAndInertialDeceleration()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 80), ScrollDeadzone = 10 };
            var canvas = new Panel { CustomMinimumSize = new Vector2(400, 400) };
            scroll.AddChild(canvas);
            var context = new UIContext(); context.Add(scroll); context.Layout();
            scroll.ScrollOffset = new Vector2(50, 50);

            var started = 0; var ended = 0;
            scroll.ScrollStarted += (_, _) => started++;
            scroll.ScrollEnded += (_, _) => ended++;

            scroll.BeginTouchDragScroll();
            Assert.That(scroll.IsTouchDragging, Is.True);

            scroll.TouchDragScrollBy(new Vector2(0, 4));
            Assert.That(scroll.ScrollOffset, Is.EqualTo(new Vector2(50, 50)), "Motion within the deadzone must not scroll yet.");
            Assert.That(scroll.IsBeyondScrollDeadzone, Is.False);
            Assert.That(started, Is.EqualTo(0));

            scroll.TouchDragScrollBy(new Vector2(0, 8));
            Assert.That(scroll.IsBeyondScrollDeadzone, Is.True, "Accumulated motion beyond the deadzone crosses it.");
            Assert.That(started, Is.EqualTo(1));
            Assert.That(scroll.ScrollOffset, Is.EqualTo(new Vector2(50, 42)), "Crossing the deadzone resets accumulation to just the latest motion for smooth continuation.");

            scroll.TouchDragScrollBy(new Vector2(0, -18));
            Assert.That(scroll.ScrollOffset, Is.EqualTo(new Vector2(50, 60)));

            scroll.Process(new GameTime(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)));
            Assert.That(scroll.TouchDragSpeed.X, Is.EqualTo(0).Within(.001f));
            Assert.That(scroll.TouchDragSpeed.Y, Is.EqualTo(200).Within(.001f), "Speed is sampled from accumulated motion during processing, mirroring Godot's ScrollContainer.");
            scroll.EndTouchDragScroll();
            Assert.That(scroll.IsTouchDragDecelerating, Is.True);
            scroll.Process(new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50)));
            Assert.That(scroll.ScrollOffset.Y, Is.EqualTo(70).Within(.001f));
            Assert.That(scroll.TouchDragSpeed.Y, Is.EqualTo(150).Within(.001f));

            scroll.Process(new GameTime(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(400)));
            Assert.That(scroll.IsTouchDragging, Is.False, "Deceleration turns itself off once both axes report a stopped condition, matching Godot's literal AND check.");
            Assert.That(scroll.IsTouchDragDecelerating, Is.False);
            Assert.That(scroll.IsBeyondScrollDeadzone, Is.False);
            Assert.That(ended, Is.EqualTo(1));

            scroll.ScrollOffset = new Vector2(50, 50);
            scroll.BeginTouchDragScroll();
            scroll.TouchDragScrollBy(new Vector2(2, 2));
            Assert.That(scroll.ScrollOffset, Is.EqualTo(new Vector2(50, 50)), "Small motion under the deadzone stays inert on a fresh drag.");
            scroll.EndTouchDragScroll();
            Assert.That(scroll.IsTouchDragging, Is.False, "Ending a drag that never crossed the deadzone cancels immediately without a speed-based deceleration phase.");
            Assert.That(ended, Is.EqualTo(1), "No scroll_ended signal fires for a drag that never crossed the deadzone.");
        }

        [Test]
        public void ScrollContainer_AutomaticallyRoutesTouchscreenPointerDrag()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 80), ScrollDeadzone = 5 };
            scroll.AddChild(new Panel { CustomMinimumSize = new Vector2(100, 300) });
            var context = new UIContext { TouchscreenAvailable = true }; context.Add(scroll); context.Layout();
            var frame = new GameTime(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
            var started = 0; scroll.ScrollStarted += (_, _) => started++;

            context.Update(frame, Mouse(50, 40), new KeyboardState());
            context.Update(frame, Mouse(50, 40, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.IsTouchDragging, Is.True);
            context.Update(frame, Mouse(50, 37, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.VerticalScroll, Is.Zero, "Motion inside the deadzone remains inert.");
            context.Update(frame, Mouse(50, 27, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.VerticalScroll, Is.EqualTo(10));
            Assert.That(started, Is.EqualTo(1));
            context.Update(frame, Mouse(50, 27), new KeyboardState());
            Assert.That(scroll.IsTouchDragDecelerating, Is.True);

            scroll.CancelTouchDragScroll(); scroll.ScrollTo(Vector2.Zero); context.TouchscreenAvailable = false;
            context.Update(frame, Mouse(50, 40, ButtonState.Pressed), new KeyboardState());
            context.Update(frame, Mouse(50, 20, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.IsTouchDragging, Is.False);
            Assert.That(scroll.VerticalScroll, Is.Zero, "Desktop pointer input does not enter the touchscreen drag path.");
        }

        [Test]
        public void ScrollContainer_AutoScrollsWhileDragHoversNearEdgeAndStopsOnRelease()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 100), ScrollOnDragHover = true, DragHoverScrollBorder = 20, DragHoverScrollSpeed = 12 };
            scroll.AddChild(new Control { CustomMinimumSize = new Vector2(100, 400) });
            var source = new DragSource { Position = new Vector2(120, 120), Size = new Vector2(40, 40) };
            var context = new UIContext(); context.Add(scroll); context.Add(source); context.Layout();
            var frame = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            context.Update(frame, Mouse(130, 130), new KeyboardState());
            context.Update(frame, Mouse(130, 130, ButtonState.Pressed), new KeyboardState());
            context.Update(frame, Mouse(50, 95, ButtonState.Pressed), new KeyboardState());

            Assert.That(context.IsDragging, Is.True);
            Assert.That(scroll.VerticalScroll, Is.EqualTo(180), "A 15px bottom-edge penetration scrolls by 15 * 12 * 1s, matching Godot's proportional edge formula.");

            context.Update(frame, Mouse(50, 95), new KeyboardState());
            var afterRelease = scroll.VerticalScroll;
            context.Update(frame, Mouse(50, 95), new KeyboardState());
            Assert.That(context.IsDragging, Is.False);
            Assert.That(scroll.VerticalScroll, Is.EqualTo(afterRelease));
        }

        [Test]
        public void TabBar_MapsGodotWheelOffsetScrolling()
        {
            var bar = new TabBar { Size = new Vector2(100, 24) };
            for (var i = 0; i < 10; i++) bar.AddTab($"Overflowing Tab {i}");
            var context = new UIContext(); context.Add(bar); context.Layout();
            Assert.That(bar.GetOffsetButtonsVisible(), Is.True, "The ten wide tabs must overflow the 100px strip for this test to exercise wheel scrolling.");
            Assert.That(bar.GetTabOffset(), Is.EqualTo(0));

            context.Update(Time, Mouse(50, 10), new KeyboardState());
            context.Update(Time, new MouseState(50, 10, -120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(bar.GetTabOffset(), Is.EqualTo(1), "Godot's WHEEL_DOWN advances the offset by one tab.");

            context.Update(Time, new MouseState(50, 10, -240, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(bar.GetTabOffset(), Is.EqualTo(2));

            context.Update(Time, new MouseState(50, 10, -120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(bar.GetTabOffset(), Is.EqualTo(1), "Godot's WHEEL_UP retreats the offset by one tab.");

            bar.TabOffset = 0;
            context.Update(Time, new MouseState(50, 10, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            context.Update(Time, new MouseState(50, 10, 120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(bar.GetTabOffset(), Is.EqualTo(0), "Godot only decrements while offset > 0; wheel-up at the start is a no-op.");

            bar.SetScrollingEnabled(false);
            context.Update(Time, new MouseState(50, 10, 240, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(bar.GetTabOffset(), Is.EqualTo(0), "Godot gates wheel scrolling on scrolling_enabled.");
        }

        [Test]
        public void TabBarsAndContainers_RearrangeTabsLikeGodotAndKeepTheSelectedItem()
        {
            var bar = new TabBar { Size = new Vector2(300, 28), TabSizing = TabBarSizingMode.Justify, DragToRearrangeEnabled = true, TabsRearrangeGroup = 4 };
            bar.AddTab("Scene"); bar.AddTab("Script"); bar.AddTab("Asset");
            var rearrangedBar = -1; bar.ActiveTabRearranged += (_, index) => rearrangedBar = index;
            var context = new UIContext(); context.Add(bar);
            context.Update(Time, Mouse(150, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(250, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(250, 10), new KeyboardState());

            var container = new TabContainer { Size = new Vector2(300, 120), DragToRearrangeEnabled = true, TabsRearrangeGroup = 4 };
            var scene = new Control { Name = "Scene" }; var script = new Control { Name = "Script" }; var asset = new Control { Name = "Asset" };
            container.AddChild(scene); container.AddChild(script); container.AddChild(asset); container.CurrentTab = 1;
            var rearrangedContainer = -1; container.ActiveTabRearranged += (_, index) => rearrangedContainer = index;
            var containerContext = new UIContext(); containerContext.Add(container);
            containerContext.Update(Time, Mouse(150, 10, ButtonState.Pressed), new KeyboardState());
            containerContext.Update(Time, Mouse(250, 10, ButtonState.Pressed), new KeyboardState());
            containerContext.Update(Time, Mouse(250, 10), new KeyboardState());
            container.SetTabTitle(0, "Scene view"); container.SetTabTooltip(0, "The 2D viewport"); container.SetTabMetadata(0, "viewport");
            container.SetTabIcon(0, null); container.SetTabButtonIcon(0, null); container.SetTabIconMaxWidth(0, 14);
            container.SetTabDisabled(0, true); container.SetTabHidden(1, true);

            Assert.That(bar.Tabs, Is.EqualTo(new[] { "Scene", "Asset", "Script" }));
            Assert.That(bar.CurrentTab, Is.EqualTo(2));
            Assert.That(rearrangedBar, Is.EqualTo(2));
            Assert.That(bar.TabsRearrangeGroup, Is.EqualTo(4));
            Assert.That(container.Children[2], Is.SameAs(script));
            Assert.That(container.CurrentTab, Is.EqualTo(2));
            Assert.That(rearrangedContainer, Is.EqualTo(2));
            Assert.That(container.TabsRearrangeGroup, Is.EqualTo(4));
            Assert.That(container.GetTabTitle(0), Is.EqualTo("Scene view"));
            Assert.That(container.GetTabTooltip(0), Is.EqualTo("The 2D viewport"));
            Assert.That(container.GetTabMetadata(0), Is.EqualTo("viewport"));
            Assert.That(container.GetTabIcon(0), Is.Null);
            Assert.That(container.GetTabButtonIcon(0), Is.Null);
            Assert.That(container.GetTabIconMaxWidth(0), Is.EqualTo(14));
            Assert.That(container.IsTabDisabled(0), Is.True);
            Assert.That(container.IsTabHidden(1), Is.True);
            Assert.That(script.Visible, Is.True);
            Assert.That(asset.Visible, Is.False);
        }

        [Test]
        public void Range_SharesStateHonorsPageAndSupportsExponentialRatios()
        {
            var first = new HSlider { MinValue = 0, MaxValue = 10, Page = 2, Step = .5f };
            first.Value = 10;
            var second = new HSlider(); second.Share(first);
            var changes = 0; first.ValueChanged += (_, _) => changes++;
            second.Value = 3.2f;
            first.SetValueNoSignal(4.7f);
            var exponential = new HSlider { MinValue = 1, MaxValue = 8, Step = 0, ExpRatio = true };
            exponential.Ratio = .5f;

            Assert.That(first.Value, Is.EqualTo(4.5f));
            Assert.That(second.Value, Is.EqualTo(4.5f));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(exponential.Value, Is.EqualTo(MathF.Sqrt(8)).Within(.001f));
        }

        [Test]
        public void RangeControls_MapGodotKeyboardStepHomeAndEndBehavior()
        {
            var horizontal = new HSlider { MinValue = 0, MaxValue = 20, Step = 1, CustomStep = 3, Value = 10 };
            horizontal.KeyPressed(Keys.Left);
            Assert.That(horizontal.Value, Is.EqualTo(7));
            horizontal.KeyPressed(Keys.Right);
            Assert.That(horizontal.Value, Is.EqualTo(10));
            horizontal.KeyPressed(Keys.End);
            Assert.That(horizontal.Value, Is.EqualTo(20));
            horizontal.KeyPressed(Keys.Home);
            Assert.That(horizontal.Value, Is.EqualTo(0));
            Assert.That(horizontal.GetCustomStep(), Is.EqualTo(3));

            var rtl = new HSlider { MinValue = 0, MaxValue = 20, Step = 1, Value = 10, LayoutDirection = LayoutDirection.RightToLeft };
            rtl.KeyPressed(Keys.Left);
            Assert.That(rtl.Value, Is.EqualTo(11));
            rtl.KeyPressed(Keys.Right);
            Assert.That(rtl.Value, Is.EqualTo(10));

            var vertical = new VSlider { MinValue = 0, MaxValue = 20, Step = 2, Value = 10 };
            vertical.KeyPressed(Keys.Up);
            Assert.That(vertical.Value, Is.EqualTo(12), "Godot Slider increases vertical values on ui_up.");
            vertical.KeyPressed(Keys.Down);
            Assert.That(vertical.Value, Is.EqualTo(10));

            var scroll = new VScrollBar { MinValue = 0, MaxValue = 100, Page = 20, Step = 1, CustomStep = 5, Value = 50 };
            scroll.KeyPressed(Keys.Up);
            Assert.That(scroll.Value, Is.EqualTo(45), "Godot ScrollBar scrolls upward toward lower values.");
            scroll.KeyPressed(Keys.Down);
            Assert.That(scroll.Value, Is.EqualTo(50));
            scroll.KeyPressed(Keys.End);
            Assert.That(scroll.Value, Is.EqualTo(80));
            scroll.KeyPressed(Keys.Home);
            Assert.That(scroll.Value, Is.EqualTo(0));
        }

        [Test]
        public void ScrollBar_MapsGodotPointerRegionsDragAndWheelPageDivisor()
        {
            var scroll = new VScrollBar { Size = new Vector2(14, 100), MinValue = 0, MaxValue = 100, Page = 20, Step = 2, Value = 40 };
            var context = new UIContext(); context.Add(scroll);

            Assert.That(scroll.GetCustomStep(), Is.EqualTo(-1), "Godot's default custom_step falls back to Range.step.");
            Assert.That(scroll.GetDecrementButtonRectangle(), Is.EqualTo(new Rectangle(0, 0, 14, 14)));
            Assert.That(scroll.GetIncrementButtonRectangle(), Is.EqualTo(new Rectangle(0, 86, 14, 14)));
            Assert.That(scroll.GetGrabberRectangle(), Is.EqualTo(new Rectangle(0, 41, 14, 18)));

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 4, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.Value, Is.EqualTo(38), "The decrement arrow uses Range.step when custom_step is negative.");
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 4), new KeyboardState());

            scroll.SetCustomStep(5);
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 96, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.Value, Is.EqualTo(44), "Explicit custom_step is applied before Range.step snapping.");
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 96), new KeyboardState());

            scroll.Value = 40;
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 20, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.Value, Is.EqualTo(20), "Clicking before the grabber scrolls one page up.");
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 20), new KeyboardState());

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 80, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.Value, Is.EqualTo(40), "Clicking after the grabber scrolls one page down.");
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 80), new KeyboardState());

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 50, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.IsDraggingGrabber, Is.True);
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 67, ButtonState.Pressed), new KeyboardState());
            // The pre-snap ratio-derived value lands exactly on a Step=2 half-boundary (65.0); Godot's
            // Range::_calc_value snap is round-half-up (floor(x/step+0.5)*step), which rounds 65/2=32.5
            // up to 33*2=66 - not .NET's round-half-to-even default, which would give 64.
            Assert.That(scroll.Value, Is.EqualTo(66), "Grabber dragging uses Godot's area-size ratio and then Range step snapping.");
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 67), new KeyboardState());
            Assert.That(scroll.IsDraggingGrabber, Is.False);

            scroll.Value = 40;
            Assert.That(scroll.PointerWheel(120), Is.True);
            Assert.That(scroll.Value, Is.EqualTo(38), "Wheel-up uses max(page / 8, step) toward lower values.");
            Assert.That(scroll.PointerWheel(-120), Is.True);
            Assert.That(scroll.Value, Is.EqualTo(40), "Wheel-down uses max(page / 8, step) toward higher values.");
        }

        [Test]
        public void ScrollBar_EndButtonsUseWheelFallbackWhenStepIsContinuous()
        {
            var scroll = new VScrollBar { Size = new Vector2(14, 100), MinValue = 0, MaxValue = 100, Page = 20, Value = 40 };
            var context = new UIContext(); context.Add(scroll);

            Assert.That(scroll.Step, Is.Zero, "A scrollbar remains continuous for grabber dragging by default.");
            context.Update(Time, Mouse(7, 96, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.Value, Is.EqualTo(42.5f), "The increment button uses page / 8 when no line step is configured.");
            context.Update(Time, Mouse(7, 96), new KeyboardState());

            context.Update(Time, Mouse(7, 4, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.Value, Is.EqualTo(40), "The decrement button applies the same fallback in the opposite direction.");
        }

        [Test]
        public void ScrollBar_HiddenStepButtonsReclaimTrackAndDoNotHandleStepClicks()
        {
            var scroll = new VScrollBar { Size = new Vector2(14, 100), MinValue = 0, MaxValue = 100, Page = 20, Step = 2, Value = 40 };
            var context = new UIContext(); context.Add(scroll);

            Assert.That(scroll.ShowStepButtons, Is.True);
            Assert.That(scroll.IsShowingStepButtons(), Is.True);
            scroll.SetShowStepButtons(false);

            Assert.That(scroll.GetMinimumSize(), Is.EqualTo(new Vector2(14, 4)));
            Assert.That(scroll.GetDecrementButtonRectangle(), Is.EqualTo(new Rectangle(0, 0, 14, 0)));
            Assert.That(scroll.GetIncrementButtonRectangle(), Is.EqualTo(new Rectangle(0, 100, 14, 0)));
            Assert.That(scroll.GetGrabberRectangle(), Is.EqualTo(new Rectangle(0, 38, 14, 23)));

            context.Update(Time, Mouse(7, 4, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.Value, Is.EqualTo(20), "The former decrement region becomes page-track space when step buttons are hidden.");
        }

        [Test]
        public void ScrollBar_DefaultThemeManifestBindsDirectionalStepButtonIcons()
        {
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in DefaultThemeIconResources.ManifestEntries)
                if (entry.Density == 1)
                    foreach (var binding in entry.Bindings)
                        bindings.Add(binding);

            Assert.That(bindings, Does.Contain("HScrollBar:decrement"));
            Assert.That(bindings, Does.Contain("HScrollBar:increment"));
            Assert.That(bindings, Does.Contain("VScrollBar:decrement"));
            Assert.That(bindings, Does.Contain("VScrollBar:increment"));
        }

        [Test]
        public void ScrollContainer_FollowFocusIgnoresItsOwnVerticalScrollbarButtons()
        {
            var content = new Control { CustomMinimumSize = new Vector2(240, 240) };
            var scroll = new ScrollContainer
            {
                Size = new Vector2(100, 100),
                FollowFocus = true,
                HorizontalScrollMode = ScrollBarVisibility.Never,
                VerticalScrollMode = ScrollBarVisibility.Always,
                Content = content,
            };
            using var context = new UIContext();
            context.Add(scroll);
            context.Layout();
            var arrow = scroll.VerticalScrollBar.GetIncrementButtonRectangle();
            var point = new Point(
                scroll.VerticalScrollBar.Bounds.X + arrow.Center.X,
                scroll.VerticalScrollBar.Bounds.Y + arrow.Center.Y);

            context.Update(Time, Mouse(point.X, point.Y), new KeyboardState());
            context.Update(Time, Mouse(point.X, point.Y, ButtonState.Pressed), new KeyboardState());
            scroll.Process(Time);

            Assert.That(scroll.ScrollOffset.X, Is.Zero);
            Assert.That(scroll.ScrollOffset.Y, Is.GreaterThan(0));
        }

        [Test]
        public void ScrollBar_HighlightsOnlyTheHoveredRegionLikeGodot()
        {
            var vertical = new VScrollBar { Size = new Vector2(14, 100), MinValue = 0, MaxValue = 100, Page = 20, Value = 40 };
            var context = new UIContext(); context.Add(vertical);

            context.Update(Time, Mouse(7, 4), new KeyboardState());
            Assert.That(vertical.IsDecrementHighlighted, Is.True);
            Assert.That(vertical.IsRangeHighlighted, Is.False);
            Assert.That(vertical.IsIncrementHighlighted, Is.False);

            context.Update(Time, Mouse(7, 50), new KeyboardState());
            Assert.That(vertical.IsDecrementHighlighted, Is.False);
            Assert.That(vertical.IsRangeHighlighted, Is.True);

            context.Update(Time, Mouse(7, 96), new KeyboardState());
            Assert.That(vertical.IsRangeHighlighted, Is.False);
            Assert.That(vertical.IsIncrementHighlighted, Is.True);

            context.Update(Time, Mouse(30, 96), new KeyboardState());
            Assert.That(vertical.IsDecrementHighlighted || vertical.IsRangeHighlighted || vertical.IsIncrementHighlighted, Is.False);

            var horizontal = new HScrollBar { Position = new Vector2(30, 0), Size = new Vector2(100, 14), MinValue = 0, MaxValue = 100, Page = 20 };
            context.Add(horizontal);
            context.Update(Time, Mouse(80, 7), new KeyboardState());
            Assert.That(horizontal.IsRangeHighlighted, Is.True, "Horizontal scrollbars classify the same central range on the X axis.");
        }

        [Test]
        public void ScrollBar_MapsGodotSmoothPageScrollTargetProcessing()
        {
            var scroll = new VScrollBar { Size = new Vector2(14, 100), MinValue = 0, MaxValue = 100, Page = 20, Step = 1, Value = 40 };
            var context = new UIContext(); context.Add(scroll);
            scroll.SetSmoothScrollEnabled(true);

            Assert.That(scroll.IsSmoothScrollEnabled(), Is.True);

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 80, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.Value, Is.EqualTo(40), "Smooth page clicks keep the current value and set a target.");
            Assert.That(scroll.TargetScroll, Is.EqualTo(60));
            Assert.That(scroll.IsSmoothScrolling, Is.True);

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 80), new KeyboardState());
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(7, 80, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.TargetScroll, Is.EqualTo(80), "Repeated smooth page clicks accumulate from the previous target.");
            Assert.That(scroll.Value, Is.EqualTo(40));

            scroll.Process(new GameTime(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)));
            Assert.That(scroll.Value, Is.EqualTo(50), "Godot smooth scrolling moves at 500 value units per second.");
            Assert.That(scroll.IsSmoothScrolling, Is.True);

            scroll.Process(new GameTime(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(60)));
            Assert.That(scroll.Value, Is.EqualTo(80));
            Assert.That(scroll.IsSmoothScrolling, Is.False);

            scroll.Value = 40;
            scroll.KeyPressed(Keys.Down);
            Assert.That(scroll.Value, Is.EqualTo(41), "Keyboard arrows remain immediate even when smooth page scrolling is enabled.");
            Assert.That(scroll.PointerWheel(-120), Is.True);
            Assert.That(scroll.Value, Is.EqualTo(44), "Wheel scrolling remains immediate, uses the page divisor, and still snaps through Range.step.");
        }

        [Test]
        public void ScrollBar_MapsGodotPanGestureAndDragNodeConfiguration()
        {
            var vertical = new VScrollBar { MinValue = 0, MaxValue = 100, Page = 20, Step = 5, Value = 40 };
            Assert.That(vertical.IsDragNodeEnabled(), Is.True);
            Assert.That(vertical.GetDragNode(), Is.EqualTo(string.Empty));

            vertical.SetDragNode("../Content");
            vertical.SetDragNodeEnabled(false);
            Assert.That(vertical.GetDragNode(), Is.EqualTo("../Content"));
            Assert.That(vertical.IsDragNodeEnabled(), Is.False);

            Assert.That(vertical.PanGesture(new Vector2(20, 0)), Is.False, "Vertical ScrollBars ignore horizontal-only pan gestures.");
            Assert.That(vertical.Value, Is.EqualTo(40));
            Assert.That(vertical.PanGesture(new Vector2(0, -2)), Is.True);
            Assert.That(vertical.Value, Is.EqualTo(35), "Godot pan gestures use at least Range.step in the delta direction.");
            Assert.That(vertical.PanGesture(new Vector2(0, 12)), Is.True);
            Assert.That(vertical.Value, Is.EqualTo(45));

            var horizontal = new HScrollBar { MinValue = 0, MaxValue = 100, Page = 20, Step = 5, Value = 40 };
            Assert.That(horizontal.PanGesture(new Vector2(-2, 30)), Is.True);
            Assert.That(horizontal.Value, Is.EqualTo(35), "Horizontal ScrollBars prefer nonzero X deltas over Y fallback.");
            Assert.That(horizontal.PanGesture(new Vector2(0, 12)), Is.True);
            Assert.That(horizontal.Value, Is.EqualTo(45), "Horizontal ScrollBars fall back to Y delta when X is zero.");
            Assert.That(horizontal.PanGesture(Vector2.Zero), Is.False);

            horizontal.SetDragNode(null);
            Assert.That(horizontal.GetDragNode(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void ScrollBar_EmitsScrollingOnlyForEffectiveUserScrollChanges()
        {
            var scroll = new VScrollBar { MinValue = 0, MaxValue = 100, Page = 20, Step = 5, Value = 40 };
            var scrolling = 0;
            scroll.Scrolling += (_, _) => scrolling++;

            scroll.Value = 45;
            Assert.That(scrolling, Is.Zero, "Programmatic set_value does not emit Godot's scrolling signal.");
            Assert.That(scroll.PanGesture(new Vector2(0, -2)), Is.True);
            Assert.That(scroll.Value, Is.EqualTo(40));
            Assert.That(scrolling, Is.EqualTo(1));

            scroll.Value = 0;
            scroll.PanGesture(new Vector2(0, -12));
            Assert.That(scrolling, Is.EqualTo(1), "A user scroll clamped to the existing value does not emit scrolling.");

            scroll.Value = 40;
            scroll.BeginDragNodeScroll();
            scroll.DragNodeScrollBy(new Vector2(0, -10));
            Assert.That(scroll.Value, Is.EqualTo(50));
            Assert.That(scrolling, Is.EqualTo(2));
        }

        [Test]
        public void ScrollBar_AutomaticallyForwardsConfiguredDragNodeInput()
        {
            var root = new Control { Size = new Vector2(200, 120) };
            var content = new Control { Name = "Content", Size = new Vector2(100, 100) };
            var scroll = new VScrollBar { Name = "Scroll", Position = new Vector2(120, 0), Size = new Vector2(14, 100), MinValue = 0, MaxValue = 100, Page = 20, Step = 0, Value = 40 };
            scroll.SetDragNode("../Content");
            root.AddChild(content); root.AddChild(scroll);
            var context = new UIContext { TouchscreenAvailable = true }; context.Add(root); context.Layout();
            var frame = new GameTime(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
            var scrolling = 0; scroll.Scrolling += (_, _) => scrolling++;

            context.Update(frame, Mouse(50, 50), new KeyboardState());
            context.Update(frame, Mouse(50, 50, ButtonState.Pressed), new KeyboardState());
            context.Update(frame, Mouse(50, 30, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.Value, Is.EqualTo(60));
            Assert.That(scroll.IsDragNodeTouching, Is.True);
            Assert.That(scrolling, Is.EqualTo(1));

            context.Update(frame, Mouse(50, 30), new KeyboardState());
            Assert.That(scroll.IsDragNodeDecelerating, Is.True);

            scroll.Process(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
            Assert.That(scroll.IsDragNodeDecelerating, Is.False);
            scroll.SetDragNode(null); scroll.Value = 40;
            context.Update(frame, Mouse(50, 50, ButtonState.Pressed), new KeyboardState());
            context.Update(frame, Mouse(50, 30, ButtonState.Pressed), new KeyboardState());
            Assert.That(scroll.Value, Is.EqualTo(40), "Changing the NodePath detaches the previous drag-node input source.");
        }

        [Test]
        public void ScrollBar_MapsGodotDragNodeTouchScrollAndDeceleration()
        {
            var scroll = new VScrollBar { MinValue = 0, MaxValue = 100, Page = 20, Step = 0, Value = 40 };

            scroll.BeginDragNodeScroll();
            Assert.That(scroll.IsDragNodeTouching, Is.True);
            scroll.DragNodeScrollBy(new Vector2(0, -10));
            Assert.That(scroll.Value, Is.EqualTo(50), "Godot drag-node touch scrolling subtracts relative motion from the scroll origin.");

            scroll.Process(new GameTime(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)));
            Assert.That(scroll.DragNodeSpeed.Y, Is.EqualTo(200).Within(.001f), "Drag-node speed is sampled from accumulated motion during processing.");

            scroll.EndDragNodeScroll();
            Assert.That(scroll.IsDragNodeDecelerating, Is.True);
            scroll.Process(new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50)));
            Assert.That(scroll.Value, Is.EqualTo(60));
            Assert.That(scroll.DragNodeSpeed.Y, Is.EqualTo(150).Within(.001f));

            scroll.Process(new GameTime(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(200)));
            Assert.That(scroll.Value, Is.EqualTo(80), "Deceleration clamps at max minus page and turns itself off.");
            Assert.That(scroll.IsDragNodeTouching, Is.False);
            Assert.That(scroll.IsDragNodeDecelerating, Is.False);

            scroll.Value = 40;
            scroll.BeginDragNodeScroll(touchscreenAvailable: false);
            scroll.DragNodeScrollBy(new Vector2(0, -10));
            Assert.That(scroll.Value, Is.EqualTo(40), "Godot's drag-node path only scrolls through the touch branch.");

            scroll.SetDragNodeEnabled(false);
            scroll.BeginDragNodeScroll();
            Assert.That(scroll.IsDragNodeTouching, Is.False);
        }

        [Test]
        public void Slider_MapsGodotTickCountBordersAndPositions()
        {
            var slider = new HSlider { Size = new Vector2(100, 20), TickCount = 5, TicksPosition = SliderTickPosition.Both };
            var innerTicks = slider.GetTickRectangles();
            slider.TicksOnBorders = true;
            var borderTicks = slider.GetTickRectangles();
            var vertical = new VSlider { Size = new Vector2(20, 100), TickCount = 3, TicksOnBorders = true, TicksPosition = SliderTickPosition.TopLeft };

            Assert.That(innerTicks, Has.Count.EqualTo(6));
            Assert.That(innerTicks[0], Is.EqualTo(new Rectangle(27, 16, 2, 4)));
            Assert.That(borderTicks, Has.Count.EqualTo(10));
            Assert.That(borderTicks[0], Is.EqualTo(new Rectangle(5, 16, 2, 4)));
            Assert.That(vertical.GetTickRectangles()[1], Is.EqualTo(new Rectangle(0, 49, 4, 2)));
        }

        [Test]
        public void Slider_ResolvesOrientationSpecificThemeIconsForBaseInstances()
        {
            var texture = (Texture2D)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var horizontalIcon = new ThemeIcon(texture, new Rectangle(0, 0, 12, 12), new Point(12, 12));
            var verticalIcon = new ThemeIcon(texture, new Rectangle(12, 0, 12, 12), new Point(12, 12));
            var theme = new Theme();
            theme.SetIcon("grabber", horizontalIcon, nameof(HSlider));
            theme.SetIcon("grabber", verticalIcon, nameof(VSlider));
            using var context = new UIContext { Theme = theme };
            var horizontal = new Slider(Orientation.Horizontal);
            var vertical = new Slider(Orientation.Vertical);
            context.Add(horizontal);
            context.Add(vertical);

            Assert.Multiple(() =>
            {
                Assert.That(horizontal.GetSliderThemeIcon("grabber"), Is.EqualTo(horizontalIcon));
                Assert.That(vertical.GetSliderThemeIcon("grabber"), Is.EqualTo(verticalIcon));
            });
        }

        [Test]
        public void Slider_GatesKeyboardAndWheelAdjustmentOnEditableAndScrollable()
        {
            var slider = new HSlider { Size = new Vector2(100, 20), MinValue = 0, MaxValue = 100, Step = 5, Value = 50 };
            var context = new UIContext(); context.Add(slider); context.Layout();
            context.Update(Time, Mouse(10, 10), new KeyboardState());

            context.Update(Time, new MouseState(10, 10, 120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(slider.Value, Is.EqualTo(55), "Godot's Slider::gui_input increases value by step on WHEEL_UP.");
            Assert.That(context.FocusedControl, Is.SameAs(slider), "Godot grabs focus on a scrollable wheel interaction.");

            context.Update(Time, new MouseState(10, 10, -120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(slider.Value, Is.EqualTo(50), "WHEEL_DOWN decreases value by step.");

            slider.Scrollable = false;
            context.Update(Time, new MouseState(10, 10, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            context.Update(Time, new MouseState(10, 10, 120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(slider.Value, Is.EqualTo(50), "scrollable=false blocks wheel adjustment.");
            slider.Scrollable = true;

            slider.Editable = false;
            slider.KeyPressed(Keys.Right);
            Assert.That(slider.Value, Is.EqualTo(50), "Godot's gui_input returns immediately when editable is false, blocking keyboard nudging too.");
            context.Update(Time, new MouseState(10, 10, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            context.Update(Time, new MouseState(10, 10, 120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(slider.Value, Is.EqualTo(50), "editable=false also blocks wheel adjustment.");

            slider.Editable = true;
            slider.KeyPressed(Keys.Right);
            Assert.That(slider.Value, Is.EqualTo(55));
        }

        [Test]
        public void Slider_TracksContinuousPointerDragLikeGodotAndFiresDragSignals()
        {
            var slider = new HSlider { Size = new Vector2(100, 20), MinValue = 0, MaxValue = 100 };
            var context = new UIContext(); context.Add(slider); context.Layout();
            var started = 0; slider.DragStarted += (_, _) => started++;
            var ended = new List<bool>(); slider.DragEnded += (_, changed) => ended.Add(changed);

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(20, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(started, Is.EqualTo(1));
            Assert.That(slider.Value, Is.EqualTo(20).Within(.01f), "Press jumps the value to the clicked ratio.");

            context.Update(Time, Mouse(70, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(slider.Value, Is.EqualTo(70).Within(.01f), "Godot's Slider::gui_input tracks the pointer continuously via a relative-motion grab while held, not just on press/release.");

            context.Update(Time, Mouse(70, 10), new KeyboardState());
            Assert.That(ended, Has.Count.EqualTo(1));
            Assert.That(ended[0], Is.True, "drag_ended reports whether the ratio actually changed during the drag.");

            ended.Clear();
            context.Update(Time, Mouse(70, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(70, 10), new KeyboardState());
            Assert.That(ended, Has.Count.EqualTo(1));
            Assert.That(ended[0], Is.False, "Clicking without moving reports no value change on release, matching Godot's is_equal_approx(value_before_dragging, ratio) check.");
        }

        [Test]
        public void Slider_ClickJumpInvertsForVerticalAndMirrorsForHorizontalRtlLikeGodot()
        {
            // Godot's Slider::gui_input treats the TOP of a vertical track as max (the standard "fader"
            // convention) and mirrors a horizontal slider's click-jump under RTL. The drag-continuation
            // path in PointerMoved already applied both inversions; only the initial click-jump did not.
            var vSlider = new VSlider { Size = new Vector2(20, 100), MinValue = 0, MaxValue = 100 };
            var vContext = new UIContext(); vContext.Add(vSlider); vContext.Layout();
            vContext.Update(Time, Mouse(10, 0, ButtonState.Pressed), new KeyboardState());
            Assert.That(vSlider.Value, Is.EqualTo(100).Within(.5f), "Clicking the top of a vertical slider must jump toward max, not min.");
            vContext.Update(Time, Mouse(10, 0), new KeyboardState());

            var hSlider = new HSlider { Size = new Vector2(100, 20), MinValue = 0, MaxValue = 100, LayoutDirection = LayoutDirection.RightToLeft };
            var hContext = new UIContext(); hContext.Add(hSlider); hContext.Layout();
            hContext.Update(Time, Mouse(0, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(hSlider.Value, Is.EqualTo(100).Within(.5f), "Clicking the left edge of an RTL horizontal slider must jump toward max, mirrored.");
        }

        [Test]
        public void ScrollBarAndProgressBar_DefaultToGodotsContinuousStepNotRangeDefault()
        {
            var scrollBar = new VScrollBar();
            Assert.That(scrollBar.Step, Is.EqualTo(0), "Godot's ScrollBar constructor calls set_step(0), unlike Range's own default step of 1.");

            var progress = new ProgressBar { MinValue = 0, MaxValue = 1, Value = 0.37f };
            Assert.That(progress.Value, Is.EqualTo(0.37f).Within(.001f), "Godot's ProgressBar constructor calls set_step(0.01), not Range's integer-snapping default of 1.");
        }

        [Test]
        public void Range_SnapsStepValuesRoundHalfUpLikeGodotCalcValue()
        {
            // Godot's Range::_calc_value snap is round-half-up (floor(x/step + 0.5) * step), not .NET's
            // default MathF.Round, which is round-half-to-even.
            var slider = new HSlider { Step = 2, MinValue = 0, MaxValue = 100 };
            slider.Value = 65;
            Assert.That(slider.Value, Is.EqualTo(66), "(65-0)/2=32.5 must floor-round up to 33*2=66, not down to 32*2=64.");

            slider.Step = 0;
            slider.UseRoundedValues = true;
            slider.Value = 2.5f;
            Assert.That(slider.Value, Is.EqualTo(3), "Godot's Math::round is round-half-away-from-zero: 2.5 rounds to 3, not .NET's round-half-to-even result of 2.");
        }

        [Test]
        public void ScrollBar_AppliesStepFloorAfterWheelFactorMultiplicationLikeGodot()
        {
            // Godot's ScrollBar::gui_input computes change = base * factor, THEN floors at Step -
            // scroll(MAX(change, step)) - not the other way around. Flooring at Step before multiplying
            // by a multi-notch wheel factor (factor > 1) over-scrolls.
            var scroll = new VScrollBar { Size = new Vector2(14, 100), MinValue = 0, MaxValue = 100, Page = 16, Step = 5, Value = 50 };
            var context = new UIContext(); context.Add(scroll);

            scroll.PointerWheel(360);
            Assert.That(scroll.Value, Is.EqualTo(45), "base=Page/8=2, change=max(2*3,5)=6, 50-6=44, snapped up to 45 - not the pre-fix max(2,5)*3=15 giving 35.");
        }

        [Test]
        public void SpinBox_CustomArrowRoundSnapsCurrentValueFirstLikeGodotArrowClicked()
        {
            // Godot's SpinBox::_arrow_clicked snaps the CURRENT value to the nearest multiple of
            // arrow_step first, and only steps-and-resnaps if that didn't move in the requested direction.
            var spin = new SpinBox { Step = 1, CustomArrowStep = 5, CustomArrowRound = true, Value = 12 };
            spin.StepArrow(true);
            Assert.That(spin.Value, Is.EqualTo(15), "snap(12,5)=10 <= 12, so it falls through to _calc_value(12+5,5)=round(17/5)*5=15, not the naive 12+5=17 ceiling-rounded to 20.");
        }

        [Test]
        public void DialogAndColorPicker_ExposeDeterministicStateChanges()
        {
            var dialog = new ConfirmationDialog { Visible = true };
            var confirmed = 0;
            dialog.Confirmed += (_, _) => confirmed++;
            dialog.Confirm();
            var picker = new ColorPicker { Color = new Color(10, 20, 30, 40) };
            var changes = 0;
            picker.ColorChanged += (_, _) => changes++;
            picker.Color = Color.Red;
            // Godot's set_pick_color (the property this setter mirrors) never fires color_changed itself
            // - only interactive commits (sliders, HTML submit, presets, shape drag) do.
            Assert.That(changes, Is.EqualTo(0));
            Assert.That(picker.Color, Is.EqualTo(Color.Red));

            picker.SetHsv(120f / 360, 1, 1);
            Assert.That(changes, Is.EqualTo(1), "SetHsv models an interactive slider/shape commit, which does fire color_changed.");
            Assert.That(picker.Color.R, Is.EqualTo(0));
            Assert.That(picker.Color.G, Is.EqualTo(255));
            Assert.That(picker.Color.B, Is.EqualTo(0));

            Assert.That(confirmed, Is.EqualTo(1));
            Assert.That(dialog.Visible, Is.False);
        }

        [Test]
        public void ColorPickerPopupPanel_SynchronizesItsPickerColor()
        {
            var popup = new ColorPickerPopupPanel();
            var changed = 0;
            popup.ColorChanged += (_, _) => changed++;
            popup.Color = Color.CornflowerBlue;

            Assert.That(popup.Picker.Color, Is.EqualTo(Color.CornflowerBlue));
            // A bare Color assignment mirrors Godot's silent set_pick_color and never fires
            // color_changed; the wrapper's ColorChanged is wired to the picker's own interactive signal.
            Assert.That(changed, Is.EqualTo(0));

            popup.Picker.SetHsv(0, 1, 1);
            Assert.That(changed, Is.EqualTo(1), "An interactive commit on the picker propagates through to the wrapper's ColorChanged.");
        }

        [Test]
        public void ColorPicker_ConvertsHsvHexAndManagesPresetsDeterministically()
        {
            var picker = new ColorPicker { DeferredMode = true };
            var changed = 0; var selected = 0;
            picker.ColorChanged += (_, _) => changed++;
            picker.PresetSelected += (_, _) => selected++;
            picker.ColorHtml = "#4080C0AA";
            var hsv = picker.GetHsv();
            picker.AddPreset(picker.Color); picker.AddPreset(picker.Color);
            picker.SelectPreset(0); picker.Commit();
            picker.SetHsv(hsv.X, hsv.Y, hsv.Z, .5f);

            Assert.That(picker.Presets.Count, Is.EqualTo(1));
            Assert.That(selected, Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo(3), "Deferred mode never reroutes non-slider changes away from Godot's sole color_changed signal.");
            Assert.That(picker.Color.A, Is.EqualTo(127));
        }

        [Test]
        public void ColorPicker_NamedColorsMatchSelectedRuntime()
        {
            var picker = new ColorPicker();
            var properties = typeof(Color).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            foreach (var property in properties)
            {
                if (property.PropertyType != typeof(Color) || property.Name == "MonoGameOrange") continue;
                picker.ColorHtml = property.Name;
                Assert.That(picker.Color, Is.EqualTo((Color)property.GetValue(null)), property.Name);
            }

            Assert.Throws<FormatException>(() => picker.ColorHtml = "MonoGameOrange");
        }

        [Test]
        public void ColorPicker_DeferredModeFlushesOneColorChangedAfterSliderDrag()
        {
            var picker = new ColorPicker { DeferredMode = true, Size = new Vector2(180, 140) };
            var changed = new List<Color>();
            picker.ColorChanged += (_, color) => changed.Add(color);
            var context = new UIContext(); context.Add(picker); context.Layout();

            context.Update(Time, Mouse(175, 30), new KeyboardState());
            context.Update(Time, Mouse(175, 30, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(175, 100, ButtonState.Pressed), new KeyboardState());
            Assert.That(changed, Is.Empty, "Godot suppresses color_changed while a deferred channel slider is actively dragging.");

            context.Update(Time, Mouse(175, 100), new KeyboardState());
            Assert.That(changed, Is.EqualTo(new[] { picker.Color }), "Godot flushes the ordinary color_changed signal once when the slider drag ends.");

            picker.ColorHtml = "#4080C0";
            Assert.That(changed.Count, Is.EqualTo(2), "Hex commits are not slider drags and remain immediate in deferred mode.");

            picker.ColorHtml = "Cornflower_Blue";
            Assert.That(picker.Color, Is.EqualTo(Color.CornflowerBlue));
            Assert.That(changed.Count, Is.EqualTo(3), "Named-color commits use the same ordinary signal path.");
            Assert.Throws<FormatException>(() => picker.ColorHtml = "MonoGameOrange", "Nonstandard framework branding is not a Godot/CSS named color.");
        }

        [Test]
        public void ColorPicker_AddPresetMovesAnExistingColorToTheBackLikeGodot()
        {
            var picker = new ColorPicker();
            picker.AddPreset(Color.Red);
            picker.AddPreset(Color.Green);
            picker.AddPreset(Color.Blue);
            Assert.That(picker.Presets, Is.EqualTo(new[] { Color.Red, Color.Green, Color.Blue }));

            picker.AddPreset(Color.Red);
            Assert.That(picker.Presets, Is.EqualTo(new[] { Color.Green, Color.Blue, Color.Red }), "Godot's ColorPicker::add_preset moves an already-present color to the back instead of leaving it in place.");
        }

        [Test]
        public void ColorPicker_ExposesGodotOkHslAndLinearChannelModes()
        {
            var picker = new ColorPicker { ColorMode = ColorPickerMode.OkHsl };
            picker.SetOkHsl(.23f, .67f, .48f, .5f);
            var hsl = picker.GetOkHsl();

            Assert.That(picker.GetChannelLabel(0), Is.EqualTo("H"));
            Assert.That(picker.GetChannelLabel(2), Is.EqualTo("L"));
            Assert.That(picker.GetChannelMaximum(0), Is.EqualTo(359));
            Assert.That(hsl.X, Is.EqualTo(.23f).Within(.015f));
            Assert.That(hsl.Y, Is.EqualTo(.67f).Within(.02f));
            Assert.That(hsl.Z, Is.EqualTo(.48f).Within(.015f));
            Assert.That(picker.Color.A, Is.EqualTo(127));

            picker.SetChannelValue(1, 35);
            Assert.That(picker.GetChannelValue(1), Is.EqualTo(35).Within(1.5));
            picker.ColorMode = ColorPickerMode.Linear;
            var linearBlue = picker.GetChannelValue(2);
            picker.SetChannelValue(2, .25f);

            Assert.That(picker.GetChannelLabel(2), Is.EqualTo("B"));
            Assert.That(linearBlue, Is.InRange(0f, 1f));
            Assert.That(picker.GetChannelValue(2), Is.EqualTo(.25f).Within(.015f));
        }

        [Test]
        public void ColorPicker_UsesGodotPickerShapesForDragAndPointerMapping()
        {
            var rectangle = new ColorPicker { Size = new Vector2(100, 100), Color = Color.Red, PickerShape = ColorPickerShape.HsvRectangle };
            var context = new UIContext(); context.Add(rectangle);
            context.Update(Time, Mouse(43, 25), new KeyboardState());
            context.Update(Time, Mouse(43, 25, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(20, 60, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(20, 60), new KeyboardState());
            var rectangleHsv = rectangle.GetHsv();

            Assert.That(rectangleHsv.X, Is.EqualTo(0).Within(.01f));
            Assert.That(rectangleHsv.Y, Is.EqualTo(20f / 86f).Within(.02f));
            Assert.That(rectangleHsv.Z, Is.EqualTo(.4f).Within(.02f));

            var wheel = new ColorPicker { Size = new Vector2(100, 100), Color = Color.Red, PickerShape = ColorPickerShape.HsvWheel };
            var wheelContext = new UIContext(); wheelContext.Add(wheel);
            wheelContext.Update(Time, Mouse(50, 95), new KeyboardState());
            wheelContext.Update(Time, Mouse(50, 95, ButtonState.Pressed), new KeyboardState());

            Assert.That(wheel.GetHsv().X, Is.EqualTo(.25f).Within(.02f));

            var okRectangle = new ColorPicker { Size = new Vector2(100, 100), PickerShape = ColorPickerShape.OkHsRectangle };
            okRectangle.SetOkHsl(0, 0, .5f);
            var okContext = new UIContext(); okContext.Add(okRectangle);
            okContext.Update(Time, Mouse(43, 25), new KeyboardState());
            okContext.Update(Time, Mouse(43, 25, ButtonState.Pressed), new KeyboardState());
            var hsl = okRectangle.GetOkHsl();

            Assert.That(hsl.X, Is.EqualTo(.5f).Within(.02f));
            Assert.That(hsl.Y, Is.EqualTo(.75f).Within(.02f));
            var unchanged = okRectangle.Color;
            okRectangle.PickerShape = ColorPickerShape.None;
            okContext.Update(Time, Mouse(10, 10), new KeyboardState());
            okContext.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(okRectangle.Color, Is.EqualTo(unchanged));
        }

        [Test]
        public void ColorPicker_TracksGodotOldColorPersistentAndRecentSwatches()
        {
            var picker = new ColorPicker { OldColor = Color.Orange, DisplayOldColor = true };
            picker.SetPresets(new[] { Color.Red, Color.Blue, Color.Red });
            for (var index = 0; index < 10; index++) picker.AddRecentPreset(new Color(index * 20, 10, 20));
            var selected = 0;
            picker.RecentPresetSelected += (_, _) => selected++;
            picker.SelectRecentPreset(0);

            Assert.That(picker.OldColor, Is.EqualTo(Color.Orange));
            Assert.That(picker.DisplayOldColor, Is.True);
            Assert.That(picker.Presets, Is.EqualTo(new[] { Color.Red, Color.Blue }));
            Assert.That(picker.RecentPresets.Count, Is.EqualTo(9));
            // Godot's _recent_preset_pressed moves the reselected color to the back (most-recent
            // position), matching AddPreset's own move-to-back reordering - so index 0 is now whatever
            // was previously at index 1, and the reselected color is now the newest (last) entry.
            Assert.That(picker.RecentPresets[0], Is.EqualTo(new Color(40, 10, 20)));
            Assert.That(picker.RecentPresets[8], Is.EqualTo(new Color(20, 10, 20)));
            Assert.That(selected, Is.EqualTo(1));

            var button = new ColorPickerButton { Color = Color.Red, EditAlpha = false };
            // A bare Picker.Color assignment mirrors Godot's silent set_pick_color and does not
            // propagate to the button (only the picker's interactive color_changed signal does); commit
            // through the channel sliders instead, which does propagate.
            button.Picker.SetChannelValue(0, 100); button.Picker.SetChannelValue(1, 149); button.Picker.SetChannelValue(2, 237);

            Assert.That(button.Picker.Color, Is.EqualTo(Color.CornflowerBlue));
            Assert.That(button.Color, Is.EqualTo(Color.CornflowerBlue));
            Assert.That(button.EditAlpha, Is.False);
        }

        [Test]
        public void ColorPicker_InteractiveCommitsPreserveAlphaWhenEditAlphaIsDisabledLikeGodot()
        {
            // Godot's set_edit_alpha only hides the alpha slider/label - it never mutates alpha itself,
            // so an interactive picker-surface commit must preserve whatever alpha is already set.
            var picker = new ColorPicker { EditAlpha = false, Color = new Color(100, 150, 200, 128), Size = new Vector2(180, 140) };
            picker.SetPickerCursorNormalized(new Vector2(0.5f, 0.5f));
            Assert.That(picker.Color.A, Is.EqualTo(128));
        }

        [Test]
        public void ColorPicker_ColorHtmlAcceptsCssShorthandAndCorrectsMalformedLengthsLikeGodot()
        {
            var picker = new ColorPicker();
            picker.ColorHtml = "#F00";
            Assert.That(picker.Color, Is.EqualTo(new Color(255, 0, 0, 255)), "3-digit CSS shorthand expands each digit.");

            picker.ColorHtml = "#0F08";
            Assert.That(picker.Color, Is.EqualTo(new Color(0, 255, 0, 136)), "4-digit CSS shorthand expands each digit including alpha.");

            picker.ColorHtml = "1";
            Assert.That(picker.Color, Is.EqualTo(new Color(0x11, 0x11, 0x11, 255)), "A single hex digit repeats to fill all six RGB digits, matching Godot's #1 -> #111111 correction.");

            picker.ColorHtml = "AB";
            Assert.That(picker.Color, Is.EqualTo(new Color(0xAB, 0xAB, 0xAB, 255)), "Two hex digits repeat three times, matching Godot's #12 -> #121212 correction.");

            picker.ColorHtml = "12345";
            Assert.That(picker.Color, Is.EqualTo(new Color(0x11, 0x22, 0x33, 0x44)), "A 5-digit code truncates to 4 then CSS-shorthand-expands, matching Godot's #12345 -> #11223344 correction.");

            picker.ColorHtml = "1234567";
            Assert.That(picker.Color, Is.EqualTo(new Color(0x12, 0x34, 0x56, 255)), "A 7-digit code truncates to 6, matching Godot's #1234567 -> #123456 correction.");
        }

        [Test]
        public void ColorPicker_ColorHtmlDiscardsTypedAlphaWhenEditAlphaIsDisabledLikeGodot()
        {
            var picker = new ColorPicker { EditAlpha = false, Color = new Color(10, 20, 30, 200) };
            picker.ColorHtml = "#FF000080";
            Assert.That(picker.Color, Is.EqualTo(new Color(255, 0, 0, 200)), "A typed alpha channel is discarded when EditAlpha is false, matching Godot's _html_submitted restoring the current alpha.");
        }

        [Test]
        public void ColorPicker_HexSubmissionAddsARecentPresetLikeGodot()
        {
            var picker = new ColorPicker();
            picker.ColorHtml = "#4080C0";
            Assert.That(picker.RecentPresets, Has.Member(new Color(0x40, 0x80, 0xC0, 255)), "Godot's _set_pick_color adds a recent preset for a hex-field commit.");
        }

        [Test]
        public void ColorPicker_ClickingTheOldColorSwatchRevertsToItLikeGodotsSampleInput()
        {
            var picker = new ColorPicker { DisplayOldColor = true, OldColor = Color.Blue, Color = Color.Red, Size = new Vector2(180, 140) };
            var changed = new List<Color>();
            picker.ColorChanged += (_, color) => changed.Add(color);
            var context = new UIContext(); context.Add(picker); context.Layout();

            context.Update(Time, Mouse(5, 5), new KeyboardState());
            context.Update(Time, Mouse(5, 5, ButtonState.Pressed), new KeyboardState());

            Assert.That(picker.Color, Is.EqualTo(Color.Blue));
            Assert.That(changed, Is.EqualTo(new[] { Color.Blue }));
        }

        [Test]
        public void ColorPicker_KeyboardAcceptRevertsVisibleOldColorSwatch()
        {
            var picker = new ColorPicker { DisplayOldColor = true, OldColor = Color.Blue, Color = Color.Red, Size = new Vector2(180, 140) };
            var changed = new List<Color>();
            picker.ColorChanged += (_, color) => changed.Add(color);
            var context = new UIContext(); context.Add(picker); picker.GrabFocus();

            context.Update(Time, Mouse(50, 50), new KeyboardState(Keys.Enter));

            Assert.That(picker.Color, Is.EqualTo(Color.Blue));
            Assert.That(changed, Is.EqualTo(new[] { Color.Blue }));
        }

        [Test]
        public void ColorPicker_MapsGodotVisibilityAndModeAccessorState()
        {
            var picker = new ColorPicker();
            var changed = 0;
            picker.ColorChanged += (_, _) => changed++;

            Assert.That(picker.IsEditingAlpha(), Is.True);
            // Godot's ColorPicker (and ColorPickerButton) both default edit_intensity to true.
            Assert.That(picker.IsEditingIntensity(), Is.True);
            Assert.That(picker.AreSwatchesEnabled(), Is.True);
            Assert.That(picker.IsDeferredMode(), Is.False);
            Assert.That(picker.IsColorizingSliders(), Is.True);
            Assert.That(picker.ArePresetsVisible(), Is.True);
            Assert.That(picker.AreModesVisible(), Is.True);
            Assert.That(picker.IsSamplerVisible(), Is.True);
            Assert.That(picker.AreSlidersVisible(), Is.True);
            Assert.That(picker.IsHexVisible(), Is.True);

            picker.SetEditAlpha(false);
            picker.SetEditIntensity(true);
            picker.SetCanAddSwatches(false);
            picker.SetColorizeSliders(false);
            picker.SetDeferredMode(true);
            picker.SetColorMode(ColorPickerMode.Linear);
            picker.SetPickerShape(ColorPickerShape.OkHlRectangle);
            picker.SetOldColor(Color.Purple);
            picker.SetDisplayOldColor(true);
            picker.SetPresetsVisible(false);
            picker.SetModesVisible(false);
            picker.SetSamplerVisible(false);
            picker.SetSlidersVisible(false);
            picker.SetHexVisible(false);
            picker.AddPreset(Color.Red);
            picker.SetPickColor(Color.CornflowerBlue);

            Assert.That(picker.IsEditingAlpha(), Is.False);
            Assert.That(picker.IsEditingIntensity(), Is.True);
            Assert.That(picker.AreSwatchesEnabled(), Is.False);
            Assert.That(picker.IsColorizingSliders(), Is.False);
            Assert.That(picker.IsDeferredMode(), Is.True);
            Assert.That(picker.GetColorMode(), Is.EqualTo(ColorPickerMode.Linear));
            Assert.That(picker.GetPickerShape(), Is.EqualTo(ColorPickerShape.OkHlRectangle));
            Assert.That(picker.GetOldColor(), Is.EqualTo(Color.Purple));
            Assert.That(picker.IsDisplayingOldColor(), Is.True);
            Assert.That(picker.ArePresetsVisible(), Is.False);
            Assert.That(picker.AreModesVisible(), Is.False);
            Assert.That(picker.IsSamplerVisible(), Is.False);
            Assert.That(picker.AreSlidersVisible(), Is.False);
            Assert.That(picker.IsHexVisible(), Is.False);
            Assert.That(picker.Presets, Is.Empty);
            Assert.That(picker.GetPickColor(), Is.EqualTo(Color.CornflowerBlue));
            // SetPickColor mirrors Godot's silent set_pick_color, which never fires color_changed itself
            // regardless of deferred_mode - deferred mode only changes WHEN an interactive commit's
            // signal fires (drag-end vs. every drag-move), not whether a programmatic assignment does.
            Assert.That(changed, Is.EqualTo(0));

            picker.SetCanAddSwatches(true);
            picker.AddPreset(Color.Red);
            Assert.That(picker.Presets, Is.EqualTo(new[] { Color.Red }));
            Assert.Throws<ArgumentOutOfRangeException>(() => picker.SetColorMode((ColorPickerMode)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => picker.SetPickerShape((ColorPickerShape)99));
        }

        [Test]
        public void ColorPickerButton_RevertsOnEscapeAndFiresPopupClosedLikeGodotModalClosed()
        {
            // Godot's ColorPickerButton::_modal_closed reverts to old_color and re-emits color_changed
            // only when the popup was dismissed via Escape (ui_cancel), and unconditionally emits
            // popup_closed regardless of the dismissal reason.
            var button = new ColorPickerButton { Color = Color.Red, Size = new Vector2(40, 40) };
            var closedCount = 0; button.PopupClosed += (_, _) => closedCount++;
            var context = new UIContext(); context.Add(button); context.Layout();

            context.Update(Time, Mouse(5, 5), new KeyboardState());
            context.Update(Time, Mouse(5, 5, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(5, 5), new KeyboardState());
            Assert.That(button.Popup.Visible, Is.True);
            Assert.That(button.Picker.OldColor, Is.EqualTo(Color.Red));

            var pureGreen = new Color(0, 255, 0, 255);
            button.Picker.SetChannelValue(0, 0); button.Picker.SetChannelValue(1, 255); button.Picker.SetChannelValue(2, 0);
            Assert.That(button.Color, Is.EqualTo(pureGreen), "Live edits while the popup is open propagate to the button.");

            button.Popup.KeyPressed(Keys.Escape);

            Assert.That(button.Popup.Visible, Is.False);
            Assert.That(button.Color, Is.EqualTo(Color.Red), "Escape reverts the button back to its pre-popup color.");
            Assert.That(button.Picker.Color, Is.EqualTo(Color.Red), "The picker itself is reverted too, so reopening starts from the reverted value.");
            Assert.That(closedCount, Is.EqualTo(1));
        }

        [Test]
        public void ColorPicker_MapsGodotShapeAwareKeyboardCursorAdjustment()
        {
            var picker = new ColorPicker { PickerShape = ColorPickerShape.HsvRectangle, KeyboardAdjustmentStep = .05f };
            picker.SetHsv(.25f, .5f, .5f, .75f);
            picker.KeyPressed(Keys.Right);
            picker.KeyPressed(Keys.Up);
            var hsv = picker.GetHsv();

            Assert.That(hsv.X, Is.EqualTo(.25f).Within(.01f));
            Assert.That(hsv.Y, Is.EqualTo(.55f).Within(.02f));
            Assert.That(hsv.Z, Is.EqualTo(.55f).Within(.02f));
            Assert.That(picker.GetPickerCursorNormalized().X, Is.EqualTo(.55f).Within(.02f));
            Assert.That(picker.GetPickerCursorNormalized().Y, Is.EqualTo(.45f).Within(.02f));
            Assert.That(picker.Color.A, Is.EqualTo(191));

            picker.SetPickerShape(ColorPickerShape.OkHlRectangle);
            picker.SetOkHsl(.4f, .3f, .5f);
            picker.KeyPressed(Keys.Left);
            picker.KeyPressed(Keys.Down);
            var hsl = picker.GetOkHsl();
            Assert.That(hsl.X, Is.EqualTo(.35f).Within(.02f));
            Assert.That(hsl.Y, Is.EqualTo(.3f).Within(.03f));
            Assert.That(hsl.Z, Is.EqualTo(.45f).Within(.03f));

            picker.SetPickerShape(ColorPickerShape.OkHslCircle);
            picker.SetOkHsl(.2f, .4f, .5f);
            picker.KeyPressed(Keys.Right);
            picker.KeyPressed(Keys.Down);
            hsl = picker.GetOkHsl();
            Assert.That(hsl.X, Is.EqualTo(.25f).Within(.02f));
            Assert.That(hsl.Y, Is.EqualTo(.45f).Within(.03f));

            var unchanged = picker.Color;
            picker.SetPickerShape(ColorPickerShape.None);
            picker.KeyPressed(Keys.Right);
            Assert.That(picker.Color, Is.EqualTo(unchanged));
            picker.SetPickerShape(ColorPickerShape.HsvRectangle);
            picker.SetPickerCursorNormalized(new Vector2(2, -1));
            Assert.That(picker.GetPickerCursorNormalized().X, Is.EqualTo(1).Within(.001f));
            Assert.That(picker.GetPickerCursorNormalized().Y, Is.EqualTo(0).Within(.001f));
        }

        [Test]
        public void ConfirmationDialog_ActivatesItsRenderedActions()
        {
            var dialog = new ConfirmationDialog { Size = new Vector2(220, 100), Visible = true };
            var confirmed = 0;
            var canceled = 0;
            dialog.Confirmed += (_, _) => confirmed++;
            dialog.Canceled += (_, _) => canceled++;
            var context = new UIContext(); context.Add(dialog);

            context.Update(Time, Mouse(185, 80), new KeyboardState());
            context.Update(Time, Mouse(185, 80, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(185, 80), new KeyboardState());
            dialog.Visible = true;
            context.Update(Time, Mouse(100, 80), new KeyboardState());
            context.Update(Time, Mouse(100, 80, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(100, 80), new KeyboardState());

            Assert.That(confirmed, Is.EqualTo(1));
            Assert.That(canceled, Is.EqualTo(1));
        }

        [Test]
        public void AcceptDialog_CanKeepOpenOnConfirmationAndIgnoreEscape()
        {
            var dialog = new AcceptDialog { Visible = true, HideOnOk = false, CloseOnEscape = false };
            var confirms = 0; dialog.Confirmed += (_, _) => confirms++;

            dialog.Confirm();
            dialog.KeyPressed(Keys.Escape);

            Assert.That(confirms, Is.EqualTo(1));
            Assert.That(dialog.Visible, Is.True);
        }

        [Test]
        public void AcceptDialog_HidesBeforeFiringConfirmedLikeGodotsOkPressed()
        {
            // Godot's AcceptDialog::_ok_pressed hides first (when hide_on_ok), then emits confirmed - a
            // Confirmed handler observing Visible mid-callback should already see it hidden.
            var dialog = new AcceptDialog { Visible = true };
            var visibleDuringCallback = true;
            dialog.Confirmed += (_, _) => visibleDuringCallback = dialog.Visible;

            dialog.Confirm();

            Assert.That(visibleDuringCallback, Is.False);
        }

        [Test]
        public void AcceptDialog_RegisterTextEnterConfirmsUnlessOkButtonDisabledLikeGodot()
        {
            var dialog = new AcceptDialog { HideOnOk = false };
            var lineEdit = new LineEdit();
            dialog.RegisterTextEnter(lineEdit);
            var confirmed = 0; dialog.Confirmed += (_, _) => confirmed++;

            lineEdit.KeyPressed(Keys.Enter);
            Assert.That(confirmed, Is.EqualTo(1), "A registered LineEdit's Enter should confirm the dialog, matching Godot's register_text_enter.");

            dialog.OkButtonDisabled = true;
            lineEdit.KeyPressed(Keys.Enter);
            Assert.That(confirmed, Is.EqualTo(1), "OkButtonDisabled must block confirming via a registered LineEdit's Enter, matching Godot's _text_submitted guard.");
        }

        [Test]
        public void AcceptDialog_MapsGodotCustomAndCancelButtonLifecycle()
        {
            var dialog = new AcceptDialog { Size = new Vector2(360, 120), Visible = true };
            var left = dialog.AddButton("Left", action: "left_action");
            var right = dialog.AddButton("Right", right: true, action: "right_action");
            var cancel = dialog.AddCancelButton("Abort");
            var actions = new List<string>();
            var canceled = 0;
            dialog.CustomAction += (_, action) => actions.Add(action);
            dialog.Canceled += (_, _) => canceled++;
            var context = new UIContext(); context.Add(dialog);

            context.Update(Time, Mouse(1, 1), new KeyboardState());
            Assert.That(left.Bounds.Left, Is.EqualTo(dialog.Bounds.Left + 8));
            Assert.That(right.Bounds.Right, Is.LessThan(cancel.Bounds.Left));
            Assert.That(cancel.Text, Is.EqualTo("Abort"));

            context.Update(Time, Mouse(left.Bounds.Center.X, left.Bounds.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(left.Bounds.Center.X, left.Bounds.Center.Y), new KeyboardState());
            context.Update(Time, Mouse(right.Bounds.Center.X, right.Bounds.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(right.Bounds.Center.X, right.Bounds.Center.Y), new KeyboardState());
            Assert.That(actions, Is.EqualTo(new[] { "left_action", "right_action" }));
            Assert.That(dialog.Visible, Is.True, "Custom actions do not close the dialog by default.");

            dialog.RemoveButton(right);
            Assert.That(right.Parent, Is.Null);
            Assert.Throws<ArgumentException>(() => dialog.RemoveButton(new Button()));

            context.Update(Time, Mouse(cancel.Bounds.Center.X, cancel.Bounds.Center.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(cancel.Bounds.Center.X, cancel.Bounds.Center.Y), new KeyboardState());
            Assert.That(canceled, Is.EqualTo(1));
            Assert.That(dialog.Visible, Is.False);
        }

        [Test]
        public void FileDialog_DefaultOkTextSurvivesButCustomOkTextSurvivesFileModeChange()
        {
            // Godot's FileDialog changes only default_ok_text per mode via set_default_ok_text; a
            // caller's custom set_ok_button_text override always wins and is never clobbered.
            var dialog = new FileDialog { FileMode = FileDialogMode.OpenFile };
            Assert.That(dialog.OkText, Is.EqualTo("Open"));

            dialog.FileMode = FileDialogMode.SaveFile;
            Assert.That(dialog.OkText, Is.EqualTo("Save"), "With no custom override, OkText tracks the mode's default.");

            dialog.OkText = "Choose";
            Assert.That(dialog.OkText, Is.EqualTo("Choose"));
            dialog.FileMode = FileDialogMode.OpenDirectory;
            Assert.That(dialog.OkText, Is.EqualTo("Choose"), "A custom OkText override survives a FileMode change instead of being clobbered.");

            dialog.OkText = null;
            Assert.That(dialog.OkText, Is.EqualTo("Select Current Folder"), "Clearing the override reverts to the mode's current default.");
        }

        [Test]
        public void FileDialog_ActivateEntryClearsStaleSelectionOnDirectoryNavigation()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            var subDirectory = Path.Combine(tempRoot, "sub");
            Directory.CreateDirectory(subDirectory);
            try
            {
                var existing = Path.Combine(tempRoot, "existing.tscn");
                File.WriteAllText(existing, "scene");
                var dialog = new FileDialog { FileMode = FileDialogMode.OpenFile };
                dialog.SetCurrentDir(tempRoot);
                dialog.SelectFile(existing);
                Assert.That(dialog.CurrentFile, Is.EqualTo(existing));

                var subIndex = FindEntryIndex(dialog, subDirectory);
                Assert.That(subIndex, Is.GreaterThanOrEqualTo(0));
                dialog.ActivateEntry(subIndex);

                // Godot's _file_list_item_activated clears the filename field on directory navigation
                // for every mode except Save, so a stale selection from a directory the user has since
                // navigated away from can't be silently confirmed.
                Assert.That(dialog.CurrentFile, Is.Empty, "Navigating into a directory must clear the previously selected file.");

                dialog.FileMode = FileDialogMode.SaveFile;
                dialog.SetCurrentDir(tempRoot);
                dialog.SelectFile(existing);
                var saveSubIndex = FindEntryIndex(dialog, subDirectory);
                dialog.ActivateEntry(saveSubIndex);
                Assert.That(dialog.CurrentFile, Is.EqualTo(existing), "Save mode is the one exception - Godot keeps the typed filename when browsing into a directory.");
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void FileDialog_DoubleClickNavigatesDirectoriesAndExposesNavigationButtons()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            var subDirectory = Path.Combine(tempRoot, "sub");
            Directory.CreateDirectory(subDirectory);
            try
            {
                var dialog = new FileDialog { FileMode = FileDialogMode.OpenFile, Visible = true, Size = new Vector2(560, 360) };
                dialog.SetCurrentDir(tempRoot);
                using var context = new UIContext { ViewportSize = new Vector2(560, 360) };
                context.Add(dialog);

                var row = new Point(196, 118);
                context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(row.X, row.Y), new KeyboardState());
                context.Update(new GameTime(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)), Mouse(row.X, row.Y, ButtonState.Pressed), new KeyboardState());
                context.Update(new GameTime(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10)), Mouse(row.X, row.Y), new KeyboardState());
                context.Update(new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(80)), Mouse(row.X, row.Y, ButtonState.Pressed), new KeyboardState());
                context.Update(new GameTime(TimeSpan.FromMilliseconds(110), TimeSpan.FromMilliseconds(10)), Mouse(row.X, row.Y), new KeyboardState());

                Assert.That(dialog.CurrentPath, Is.EqualTo(subDirectory));
                Assert.That(dialog.CurrentFile, Is.Empty);
                Assert.That(dialog.CanGoBack, Is.True);
                Assert.That(dialog.GetMinimumSize(), Is.EqualTo(new Vector2(640, 420)));
                var backButton = dialog.Children.OfType<Button>().Single(button => button.Name == "FileDialogBack");
                var forwardButton = dialog.Children.OfType<Button>().Single(button => button.Name == "FileDialogForward");
                var upButton = dialog.Children.OfType<Button>().Single(button => button.Name == "FileDialogUp");
                Assert.Multiple(() =>
                {
                    Assert.That(backButton.Enabled, Is.True);
                    Assert.That(forwardButton.Enabled, Is.False);
                    Assert.That(upButton.Enabled, Is.True);
                    Assert.That(new[] { backButton, forwardButton, upButton }, Has.All.Property(nameof(BaseButton.DecorativeIconProvider)).Not.Null);
                    Assert.That(new[] { backButton, forwardButton, upButton }, Has.All.Property(nameof(BaseButton.HideTextWhenDecorativeIconAvailable)).True);
                    Assert.That(new[] { backButton, forwardButton, upButton }.Select(button => button.GetIconRectangle(new Vector2(16, 16))),
                        Is.All.EqualTo(new Rectangle(7, 7, 16, 16)));
                    var pathEdit = dialog.Children.OfType<LineEdit>().Single(edit => edit.Name == "FileDialogPath");
                    Assert.That(pathEdit.Size.Y, Is.EqualTo(30));
                    Assert.That(pathEdit.Text, Is.EqualTo(subDirectory));
                    Assert.That(dialog.Children.OfType<LineEdit>().Single(edit => edit.Name == "FileDialogFilename").Size.Y, Is.EqualTo(30));
                    Assert.That(dialog.Children.OfType<OptionButton>().Single(option => option.Name == "FileDialogFilter").Size.X, Is.GreaterThanOrEqualTo(150));
                    Assert.That(dialog.Children.OfType<OptionButton>().Single(option => option.Name == "FileDialogSort").Size, Is.EqualTo(new Vector2(200, 30)));
                    Assert.That(new Point(dialog.DialogCancelButtonBounds.Width, dialog.DialogCancelButtonBounds.Height), Is.EqualTo(new Point(80, 32)));
                    Assert.That(new Point(dialog.DialogOkButtonBounds.Width, dialog.DialogOkButtonBounds.Height), Is.EqualTo(new Point(80, 32)));
                    Assert.That(dialog.DialogOkButtonBounds.Left - dialog.DialogCancelButtonBounds.Right, Is.EqualTo(8));
                });

                context.Update(new GameTime(TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(10)), Mouse(25, 47, ButtonState.Pressed), new KeyboardState());
                context.Update(new GameTime(TimeSpan.FromMilliseconds(130), TimeSpan.FromMilliseconds(10)), Mouse(25, 47), new KeyboardState());
                Assert.That(dialog.CurrentPath, Is.EqualTo(tempRoot), "Back should return to the previous directory.");

                context.Update(new GameTime(TimeSpan.FromMilliseconds(140), TimeSpan.FromMilliseconds(10)), Mouse(61, 47, ButtonState.Pressed), new KeyboardState());
                context.Update(new GameTime(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(10)), Mouse(61, 47), new KeyboardState());
                Assert.That(dialog.CurrentPath, Is.EqualTo(subDirectory), "Forward should restore the directory left by Back.");

                context.Update(new GameTime(TimeSpan.FromMilliseconds(160), TimeSpan.FromMilliseconds(10)), Mouse(97, 47, ButtonState.Pressed), new KeyboardState());
                context.Update(new GameTime(TimeSpan.FromMilliseconds(170), TimeSpan.FromMilliseconds(10)), Mouse(97, 47), new KeyboardState());
                Assert.That(dialog.CurrentPath, Is.EqualTo(tempRoot), "Up should navigate to the parent directory.");
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        private static int FindEntryIndex(FileDialog dialog, string path)
        {
            var target = Path.GetFullPath(path);
            for (var index = 0; index < dialog.Entries.Count; index++)
                if (Path.GetFullPath(dialog.Entries[index]) == target) return index;
            return -1;
        }

        [Test]
        public void FileDialog_TracksMultiSelectionAndEmitsSelectedFiles()
        {
            var dialog = new FileDialog { FileMode = FileDialogMode.OpenFiles };
            var firstPath = Path.GetFullPath(Path.Combine("tmp", "first.txt"));
            var secondPath = Path.GetFullPath(Path.Combine("tmp", "second.txt"));
            var selected = new string[0];
            dialog.FilesSelected += (_, files) =>
            {
                selected = new string[files.Count];
                for (var index = 0; index < files.Count; index++) selected[index] = files[index];
            };
            dialog.SelectFile(firstPath);
            dialog.SelectFile(secondPath, append: true);
            dialog.Confirm();
            Assert.That(selected, Is.EqualTo(new[] { firstPath, secondPath }));
        }

        [Test]
        public void FileDialog_ExposesGodotStyleCurrentPathFiltersAndDirectoryConfirmation()
        {
            var dialog = new FileDialog { FileMode = FileDialogMode.OpenDirectory, FilenameFilter = "*.cs;*.gd" };
            dialog.SetFilters(new[] { "*.cs", "*.gd" });
            dialog.SetCurrentDir(Directory.GetCurrentDirectory());
            dialog.SetCurrentFile("scene.tscn");
            var selectedDirectory = string.Empty; dialog.DirectorySelected += (_, path) => selectedDirectory = path;
            dialog.Confirm();

            Assert.That(dialog.GetCurrentDir(), Is.EqualTo(Path.GetFullPath(Directory.GetCurrentDirectory())));
            Assert.That(dialog.GetCurrentFile(), Is.EqualTo(Path.GetFullPath("scene.tscn")));
            Assert.That(dialog.Filters, Is.EqualTo(new[] { "*.cs", "*.gd" }));
            Assert.That(selectedDirectory, Is.EqualTo(dialog.CurrentPath));
        }

        [Test]
        public void FileDialog_MapsGodotFileModeTitleAndActionTextPolicy()
        {
            var dialog = new FileDialog();

            Assert.That(dialog.GetFileMode(), Is.EqualTo(FileDialogMode.OpenFile));
            Assert.That(dialog.OkText, Is.EqualTo("Open"));
            Assert.That(dialog.Title, Is.EqualTo("Open a File"));
            Assert.That(dialog.IsModeOverridingTitle(), Is.True);

            dialog.SetFileMode(FileDialogMode.OpenFiles);
            Assert.That(dialog.OkText, Is.EqualTo("Open"));
            Assert.That(dialog.Title, Is.EqualTo("Open File(s)"));

            dialog.SetFileMode(FileDialogMode.OpenDirectory);
            Assert.That(dialog.OkText, Is.EqualTo("Select Current Folder"));
            Assert.That(dialog.Title, Is.EqualTo("Open a Directory"));

            dialog.SetFileMode(FileDialogMode.OpenAny);
            Assert.That(dialog.OkText, Is.EqualTo("Open"));
            Assert.That(dialog.Title, Is.EqualTo("Open a File or Directory"));

            dialog.SetFileMode(FileDialogMode.SaveFile);
            Assert.That(dialog.OkText, Is.EqualTo("Save"));
            Assert.That(dialog.Title, Is.EqualTo("Save a File"));

            dialog.Title = "Custom title";
            dialog.SetModeOverridesTitle(false);
            dialog.SetFileMode(FileDialogMode.OpenFile);

            Assert.That(dialog.OkText, Is.EqualTo("Open"));
            Assert.That(dialog.Title, Is.EqualTo("Custom title"));
            Assert.That(dialog.ModeOverridesTitle, Is.False);
        }

        [Test]
        public void FileDialog_MapsGodotCurrentPathSplitAndJoin()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var scene = Path.Combine(tempRoot, "scene.tscn");
                File.WriteAllText(scene, "scene");
                var dialog = new FileDialog();

                dialog.SetCurrentPath(scene);

                Assert.That(dialog.GetCurrentDir(), Is.EqualTo(tempRoot));
                Assert.That(dialog.GetCurrentPath(), Is.EqualTo(scene));
                Assert.That(dialog.GetCurrentFile(), Is.EqualTo(scene));
                Assert.That(dialog.GetSelectedFiles(), Is.EqualTo(new[] { scene }));

                dialog.SetCurrentPath(string.Empty);

                Assert.That(dialog.GetCurrentDir(), Is.EqualTo(tempRoot));
                Assert.That(dialog.GetCurrentPath(), Is.EqualTo(scene));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void FileDialog_MapsGodotSaveOverwriteWarningAndCustomizationState()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var existing = Path.Combine(tempRoot, "scene.tscn");
                File.WriteAllText(existing, "old scene");
                var dialog = new FileDialog { FileMode = FileDialogMode.SaveFile, Visible = true, Size = new Vector2(320, 160) };
                dialog.SetCurrentDir(tempRoot);
                dialog.SetFilters(new[] { "*.tscn" });
                var selected = string.Empty;
                var confirmed = 0;
                dialog.FileSelected += (_, path) => selected = path;
                dialog.Confirmed += (_, _) => confirmed++;

                Assert.That(dialog.OverwriteWarningEnabled, Is.True);
                Assert.That(dialog.IsCustomizationFlagEnabled(FileDialogCustomization.OverwriteWarning), Is.True);
                dialog.SetCurrentFile("scene");
                dialog.Confirm();

                Assert.That(dialog.CurrentFile, Is.EqualTo(existing));
                Assert.That(dialog.PendingOverwritePath, Is.EqualTo(existing));
                Assert.That(dialog.IsOverwriteConfirmationVisible, Is.True);
                Assert.That(dialog.Visible, Is.True, "The parent dialog remains open while Godot's nested overwrite confirmation is pending.");
                Assert.That(selected, Is.Empty);
                // Godot's FileDialog connects its own `confirmed` signal to _action_pressed, so
                // `confirmed` fires unconditionally the instant OK is pressed - decoupled from whether
                // the save is actually valid or a nested overwrite confirmation is still pending.
                Assert.That(confirmed, Is.EqualTo(1));

                dialog.CancelPendingOverwrite();
                Assert.That(dialog.PendingOverwritePath, Is.Empty);
                Assert.That(dialog.IsOverwriteConfirmationVisible, Is.False);
                Assert.That(selected, Is.Empty);

                dialog.Confirm();
                dialog.ConfirmPendingOverwrite();

                Assert.That(selected, Is.EqualTo(existing));
                // Two total Confirm() calls have fired by this point (the first, pending-overwrite one
                // above, plus this one), and `confirmed` fires unconditionally on each.
                Assert.That(confirmed, Is.EqualTo(2));
                Assert.That(dialog.Visible, Is.False);

                dialog.Visible = true;
                dialog.SetCustomizationFlagEnabled(FileDialogCustomization.OverwriteWarning, false);
                selected = string.Empty;
                confirmed = 0;
                dialog.SetCurrentFile("scene");
                dialog.Confirm();

                Assert.That(dialog.OverwriteWarningEnabled, Is.False);
                Assert.That(dialog.IsOverwriteConfirmationVisible, Is.False);
                Assert.That(selected, Is.EqualTo(existing));
                Assert.That(confirmed, Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void FileDialog_FilterIndexNarrowsTheFileListLikeGodot()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                File.WriteAllText(Path.Combine(tempRoot, "a.png"), string.Empty);
                File.WriteAllText(Path.Combine(tempRoot, "b.txt"), string.Empty);
                var dialog = new FileDialog();
                dialog.SetFilters(new[] { "*.png", "*.txt" });

                dialog.FilterIndex = 0;
                dialog.SetCurrentDir(tempRoot);
                Assert.That(dialog.Entries.Count, Is.EqualTo(2), "Index 0 (All Recognized) combines every registered filter when there's more than one.");

                dialog.FilterIndex = 1;
                Assert.That(dialog.Entries.Count, Is.EqualTo(1));
                Assert.That(Path.GetFileName(dialog.Entries[0]), Is.EqualTo("a.png"));

                dialog.FilterIndex = 2;
                Assert.That(dialog.Entries.Count, Is.EqualTo(1));
                Assert.That(Path.GetFileName(dialog.Entries[0]), Is.EqualTo("b.txt"));

                dialog.FilterIndex = 3;
                Assert.That(dialog.Entries.Count, Is.EqualTo(2), "The last filter index is always All Files (match everything), matching Godot.");
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void FileDialog_GoBackAndGoForwardNavigateHistoryLikeGodot()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            var sub = Path.Combine(tempRoot, "sub");
            Directory.CreateDirectory(sub);
            try
            {
                var dialog = new FileDialog();
                dialog.NavigateTo(tempRoot);
                dialog.NavigateTo(sub);

                Assert.That(dialog.CanGoBack, Is.True);
                Assert.That(dialog.CanGoForward, Is.False);

                dialog.GoBack();
                Assert.That(dialog.CurrentPath, Is.EqualTo(Path.GetFullPath(tempRoot)));
                Assert.That(dialog.CanGoForward, Is.True);

                dialog.GoForward();
                Assert.That(dialog.CurrentPath, Is.EqualTo(Path.GetFullPath(sub)));
                Assert.That(dialog.CanGoForward, Is.False);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void FileDialog_FolderCreationIsForceDisabledForOpenFileModesLikeGodot()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var dialog = new FileDialog { FileMode = FileDialogMode.OpenFile };
                dialog.SetCurrentDir(tempRoot);

                Assert.That(dialog.EffectiveCanCreateFolders, Is.False, "Open File mode force-disables folder creation regardless of CanCreateFolders.");
                Assert.That(() => dialog.CreateFolder("NewFolder"), Throws.InvalidOperationException);

                dialog.FileMode = FileDialogMode.SaveFile;
                Assert.That(dialog.EffectiveCanCreateFolders, Is.True);
                dialog.CreateFolder("NewFolder");
                Assert.That(Directory.Exists(Path.Combine(tempRoot, "NewFolder")), Is.True);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void FileDialog_RefreshShowsAGracefulMessageOnPermissionDeniedLikeGodot()
        {
            if (OperatingSystem.IsWindows()) { Assert.Ignore("Unix file permissions are not portable to Windows."); return; }
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                File.SetUnixFileMode(tempRoot, UnixFileMode.None);
                var dialog = new FileDialog();

                dialog.Refresh(tempRoot);

                Assert.That(dialog.Entries, Is.Empty);
                Assert.That(dialog.Message, Is.Not.Empty, "A permission-denied directory should surface a graceful message instead of throwing, matching Godot's is_readable() check.");
            }
            finally
            {
                File.SetUnixFileMode(tempRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void FileDialog_ReportsUnavailableFilesystemWithoutEnumerating()
        {
            var fileSystem = new UnavailableFileDialogFileSystem();
            var dialog = new FileDialog { FileSystem = fileSystem };

            Assert.DoesNotThrow(() => dialog.Refresh("/unavailable"));
            Assert.That(dialog.IsFileSystemAvailable, Is.False);
            Assert.That(dialog.Entries, Is.Empty);
            Assert.That(dialog.Message, Is.EqualTo("Filesystem access is unavailable."));
            Assert.That(fileSystem.Operations, Is.Zero);
        }

        [Test]
        public void FileDialog_SuppressesInvalidOpenActionsAndAppendsSaveExtension()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var open = new FileDialog { FileMode = FileDialogMode.OpenFile };
                var openSelected = string.Empty;
                open.FileSelected += (_, path) => openSelected = path;
                open.Confirm();
                Assert.That(openSelected, Is.Empty);

                open.SetCurrentFile(Path.Combine(tempRoot, "missing.tscn"));
                open.Confirm();
                Assert.That(openSelected, Is.Empty);

                var existing = Path.Combine(tempRoot, "existing.tscn");
                File.WriteAllText(existing, "scene");
                open.SetCurrentFile(existing);
                open.Confirm();
                Assert.That(openSelected, Is.EqualTo(existing));

                var save = new FileDialog { FileMode = FileDialogMode.SaveFile, OverwriteWarningEnabled = false };
                save.SetCurrentDir(tempRoot);
                save.SetFilters(new[] { "*.tscn" });
                var saved = string.Empty;
                save.FileSelected += (_, path) => saved = path;
                save.SetCurrentFile("new_scene");
                save.Confirm();

                Assert.That(saved, Is.EqualTo(Path.Combine(tempRoot, "new_scene.tscn")));
                Assert.That(save.CurrentFile, Is.EqualTo(saved));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        private sealed class UnavailableFileDialogFileSystem : IFileDialogFileSystem
        {
            public bool IsAvailable => false;
            public int Operations { get; private set; }
            public string GetCurrentDirectory() { Operations++; throw new PlatformNotSupportedException(); }
            public bool FileExists(string path) { Operations++; throw new PlatformNotSupportedException(); }
            public bool DirectoryExists(string path) { Operations++; throw new PlatformNotSupportedException(); }
            public IEnumerable<string> EnumerateEntries(string path) { Operations++; throw new PlatformNotSupportedException(); }
            public string GetParentDirectory(string path) { Operations++; throw new PlatformNotSupportedException(); }
            public void CreateDirectory(string path) { Operations++; throw new PlatformNotSupportedException(); }
            public DateTime GetLastWriteTimeUtc(string path) { Operations++; throw new PlatformNotSupportedException(); }
        }

        [Test]
        public void FileDialog_MapsGodotFavoriteAndRecentDirectoryLists()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            var first = Path.Combine(tempRoot, "first");
            var second = Path.Combine(tempRoot, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            try
            {
                FileDialog.SetFavoriteList(new[] { first, second + "/" });
                Assert.That(FileDialog.GetFavoriteList(), Is.EqualTo(new[] { first.Replace('\\', '/') + "/", second.Replace('\\', '/') + "/" }));

                var dialog = new FileDialog();
                dialog.SetCurrentDir(first);
                dialog.ToggleCurrentDirectoryFavorite();
                Assert.That(FileDialog.GetFavoriteList(), Is.EqualTo(new[] { second.Replace('\\', '/') + "/" }));
                dialog.ToggleCurrentDirectoryFavorite();
                Assert.That(FileDialog.GetFavoriteList(), Is.EqualTo(new[] { second.Replace('\\', '/') + "/", first.Replace('\\', '/') + "/" }));

                FileDialog.SetRecentList(new[] { second });
                var existing = Path.Combine(first, "scene.tscn");
                File.WriteAllText(existing, "scene");
                dialog.FileMode = FileDialogMode.OpenFile;
                dialog.SetCurrentFile(existing);
                dialog.Confirm();

                Assert.That(FileDialog.GetRecentList(), Is.EqualTo(new[] { first.Replace('\\', '/') + "/", second.Replace('\\', '/') + "/" }));

                dialog.FileMode = FileDialogMode.SaveFile;
                dialog.OverwriteWarningEnabled = false;
                dialog.SetCurrentDir(second);
                dialog.SetCurrentFile("saved.tscn");
                dialog.Confirm();

                Assert.That(FileDialog.GetRecentList(), Is.EqualTo(new[] { second.Replace('\\', '/') + "/", first.Replace('\\', '/') + "/" }));
            }
            finally
            {
                FileDialog.SetFavoriteList(Array.Empty<string>());
                FileDialog.SetRecentList(Array.Empty<string>());
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void FileDialog_RetainedControlsTrackModeCustomizationAndGodotIcons()
        {
            var dialog = new FileDialog { Size = new Vector2(560, 360), FileMode = FileDialogMode.OpenFile };
            var createFolder = dialog.Children.OfType<Button>().Single(button => button.Name == "FileDialogCreateFolder");
            var filenameFilter = dialog.Children.OfType<LineEdit>().Single(edit => edit.Name == "FileDialogFilenameFilter");
            var iconButtons = dialog.Children.OfType<Button>().Where(button => new[]
            {
                "FileDialogRefresh", "FileDialogFavorite", "FileDialogCreateFolder", "FileDialogShowHidden",
                "FileDialogThumbnails", "FileDialogList", "FileDialogFilenameFilterToggle",
                "FileDialogFavoriteUp", "FileDialogFavoriteDown",
            }.Contains(button.Name)).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(createFolder.Visible, Is.False);
                Assert.That(iconButtons, Has.Length.EqualTo(9));
                Assert.That(iconButtons, Has.All.Property(nameof(BaseButton.DecorativeIconProvider)).Not.Null);
                Assert.That(iconButtons, Has.All.Property(nameof(BaseButton.HideTextWhenDecorativeIconAvailable)).True);
                Assert.That(dialog.Children.OfType<OptionButton>().Single(option => option.Name == "FileDialogSort").DecorativeIconProvider, Is.Not.Null);
            });

            dialog.FileMode = FileDialogMode.SaveFile;
            Assert.That(createFolder.Visible, Is.True, "Changing mode should immediately update folder-creation controls.");
            dialog.CanCreateFolders = false;
            Assert.That(createFolder.Visible, Is.False, "Changing CanCreateFolders should immediately update retained controls.");

            dialog.ShowFilenameFilter = true;
            Assert.That(filenameFilter.Visible, Is.True);
            dialog.SetCustomizationFlagEnabled(FileDialogCustomization.FileFilter, false);
            Assert.That(filenameFilter.Visible, Is.False, "Disabling filename-filter customization should hide its retained editor.");
        }

        [Test]
        public void FileDialog_FavoriteControlsReorderTheSelectedDirectory()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            var first = Path.Combine(tempRoot, "first");
            var second = Path.Combine(tempRoot, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            try
            {
                FileDialog.SetFavoriteList(new[] { first, second });
                var dialog = new FileDialog { Visible = true, Size = new Vector2(560, 360) };
                dialog.SetCurrentDir(first);
                using var context = new UIContext { ViewportSize = dialog.Size };
                context.Add(dialog);
                context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(1, 1), new KeyboardState());

                context.Update(new GameTime(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)), Mouse(20, 180, ButtonState.Pressed), new KeyboardState());
                context.Update(new GameTime(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10)), Mouse(20, 180), new KeyboardState());
                context.Update(new GameTime(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(10)), Mouse(119, 119, ButtonState.Pressed), new KeyboardState());
                context.Update(new GameTime(TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10)), Mouse(119, 119), new KeyboardState());

                Assert.That(FileDialog.GetFavoriteList(), Is.EqualTo(new[] { second.Replace('\\', '/') + "/", first.Replace('\\', '/') + "/" }));
            }
            finally
            {
                FileDialog.SetFavoriteList(Array.Empty<string>());
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void FileDialog_DelegatesNativePopupAndConfinesNavigationToItsRoot()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MonoGameUiFileDialog_" + Guid.NewGuid().ToString("N"));
            var root = Path.Combine(tempRoot, "root");
            var outside = Path.Combine(tempRoot, "outside");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outside);
            try
            {
                var called = false;
                FileDialog.NativeDialogHandler = _ => { called = true; return true; };
                var dialog = new FileDialog { UseNativeDialog = true, Visible = false };

                Assert.That(dialog.PopupFileDialog(), Is.True);
                Assert.That(called, Is.True);
                Assert.That(dialog.Visible, Is.False, "A handled native popup should not open the retained dialog.");

                dialog.SetRootSubfolder(root);
                Assert.That(dialog.CurrentPath, Is.EqualTo(root));
                Assert.That(() => dialog.NavigateTo(outside), Throws.InstanceOf<UnauthorizedAccessException>());
                dialog.GoUp();
                Assert.That(dialog.CurrentPath, Is.EqualTo(root), "GoUp must stop at the configured root.");
            }
            finally
            {
                FileDialog.NativeDialogHandler = null;
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void FileDialog_MapsGodotCustomOptionsAndSelectedOptions()
        {
            var dialog = new FileDialog();
            dialog.AddOption("Include hidden", Array.Empty<string>(), 1);
            dialog.AddOption("Encoding", new[] { "UTF-8", "UTF-16", "Latin-1" }, 9);

            Assert.That(dialog.GetOptionCount(), Is.EqualTo(2));
            Assert.That(dialog.GetOptionName(0), Is.EqualTo("Include hidden"));
            Assert.That(dialog.GetOptionValues(0), Is.Empty);
            Assert.That(dialog.GetOptionDefault(0), Is.EqualTo(1));
            Assert.That(dialog.GetOptionName(1), Is.EqualTo("Encoding"));
            Assert.That(dialog.GetOptionValues(1), Is.EqualTo(new[] { "UTF-8", "UTF-16", "Latin-1" }));
            Assert.That(dialog.GetOptionDefault(1), Is.EqualTo(2), "Godot clamps option defaults to the available values.");

            var selected = dialog.GetSelectedOptions();
            Assert.That(selected["Include hidden"], Is.EqualTo(true));
            Assert.That(selected["Encoding"], Is.EqualTo(2));

            dialog.SetOptionName(-1, "Format");
            dialog.SetOptionValues(-1, new[] { "Text", "Binary" });
            dialog.SetOptionDefault(-1, -3);
            dialog.SetOptionValues(0, Array.Empty<string>());
            dialog.SetOptionDefault(0, 4);

            Assert.That(dialog.GetOptionName(1), Is.EqualTo("Format"));
            Assert.That(dialog.GetOptionValues(1), Is.EqualTo(new[] { "Text", "Binary" }));
            Assert.That(dialog.GetOptionDefault(1), Is.EqualTo(0));
            Assert.That(dialog.GetSelectedOptions()["Include hidden"], Is.EqualTo(true));

            dialog.SetOptionCount(3);
            Assert.That(dialog.GetOptionCount(), Is.EqualTo(3));
            Assert.That(dialog.GetOptionName(2), Is.EqualTo(string.Empty));
            dialog.SetOptionCount(1);
            Assert.That(dialog.GetOptionCount(), Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => dialog.SetOptionCount(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => dialog.GetOptionName(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => dialog.SetOptionName(-2, "Invalid"));
        }

        [Test]
        public void GraphEdit_TracksConnectionsSelectionAndNodeDragging()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 240), ShowMenu = false };
            var source = new GraphNode { Name = "Source", Position = new Vector2(20, 20), Size = new Vector2(100, 60) };
            source.AddOutputPort("value");
            var target = new GraphNode { Name = "Target", Position = new Vector2(220, 120), Size = new Vector2(100, 60) };
            target.AddInputPort("value");
            graph.AddChild(source); graph.AddChild(target);
            var context = new UIContext(); context.Add(graph);

            Assert.That(graph.ConnectNode("Source", 0, "Target", 0), Is.True);
            Assert.That(graph.ConnectNode("Missing", 0, "Target", 0), Is.False);
            context.Update(Time, Mouse(30, 30), new KeyboardState());
            context.Update(Time, Mouse(30, 30, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(70, 50, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(70, 50), new KeyboardState());

            Assert.That(source.Selected, Is.True);
            Assert.That(source.Position, Is.EqualTo(new Vector2(60, 40)));
            Assert.That(graph.Connections.Count, Is.EqualTo(1));
        }

        [Test]
        public void GraphEdit_TransformsLogicalNodeCoordinatesForZoomAndScroll()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 240), ZoomMin = .5f, ZoomMax = 2f };
            var node = new GraphNode { Name = "Node", Position = new Vector2(100, 50), Size = new Vector2(100, 60) };
            graph.AddChild(node);
            var context = new UIContext(); context.Add(graph);

            graph.SetZoomCustom(2f, Vector2.Zero);
            graph.ScrollOffset = new Vector2(20, 10);
            context.Layout();

            Assert.That(node.PositionOffset, Is.EqualTo(new Vector2(100, 50)));
            Assert.That(node.Bounds, Is.EqualTo(new Rectangle(180, 90, 200, 120)));
            Assert.That(graph.GraphToScreen(node.PositionOffset), Is.EqualTo(new Vector2(180, 90)));
            graph.Zoom = 8f;
            Assert.That(graph.Zoom, Is.EqualTo(2f));
            graph.Zoom = .1f;
            Assert.That(graph.Zoom, Is.EqualTo(.5f));
        }

        [Test]
        public void GraphEdit_ArrangesConnectedNodesAndPansItsCanvas()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 240) };
            var source = new GraphNode { Name = "Source", Position = new Vector2(20, 30), Size = new Vector2(100, 60) };
            var branch = new GraphNode { Name = "Branch", Position = new Vector2(40, 160), Size = new Vector2(80, 50) };
            var target = new GraphNode { Name = "Target", Position = new Vector2(240, 90), Size = new Vector2(120, 70) };
            graph.AddChild(source); graph.AddChild(branch); graph.AddChild(target);
            graph.ConnectNode("Source", 0, "Target", 0); graph.ConnectNode("Branch", 0, "Target", 0);
            var arranged = 0; graph.NodesArranged += _ => arranged++;
            graph.ArrangeNodes();
            graph.Panner.BeginPan(new Point(10, 10)); graph.Panner.UpdatePan(new Point(30, 25)); graph.Panner.EndPan();
            graph.Panner.ApplyWheel(120, false, false, Vector2.Zero);

            Assert.That(source.Position.X, Is.EqualTo(20));
            Assert.That(branch.Position.X, Is.EqualTo(20));
            Assert.That(target.Position.X, Is.EqualTo(220));
            Assert.That(branch.Position.Y, Is.EqualTo(190));
            Assert.That(graph.ScrollOffset.X, Is.EqualTo(-22).Within(.001));
            Assert.That(graph.ScrollOffset.Y, Is.EqualTo(-16.5).Within(.001));
            Assert.That(graph.Zoom, Is.EqualTo(1.1f).Within(.001));
            Assert.That(arranged, Is.EqualTo(1));
        }

        [Test]
        public void GraphNode_MapsGodotSlotsToTypedColoredPorts()
        {
            var node = new GraphNode { Size = new Vector2(120, 100) };
            var leftIcon = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var rightIcon = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var updated = new List<int>();
            node.SlotUpdated += (_, slot) => updated.Add(slot);
            node.SetSlot(2, true, 7, Color.Orange, true, 3, Color.CornflowerBlue, leftIcon, rightIcon, false);

            Assert.That(node.InputPortCount, Is.EqualTo(1));
            Assert.That(node.OutputPortCount, Is.EqualTo(1));
            Assert.That(node.IsSlotEnabledLeft(2), Is.True);
            Assert.That(node.IsSlotEnabledRight(2), Is.True);
            Assert.That(node.GetSlotTypeLeft(2), Is.EqualTo(7));
            Assert.That(node.GetSlotTypeRight(2), Is.EqualTo(3));
            Assert.That(node.GetSlotColorLeft(2), Is.EqualTo(Color.Orange));
            Assert.That(node.GetSlotColorRight(2), Is.EqualTo(Color.CornflowerBlue));
            Assert.That(node.GetSlotCustomIconLeft(2), Is.SameAs(leftIcon));
            Assert.That(node.GetSlotCustomIconRight(2), Is.SameAs(rightIcon));
            Assert.That(node.IsSlotDrawStyleBox(2), Is.False);
            Assert.That(node.GetInputPortSlot(0), Is.EqualTo(2));
            Assert.That(node.GetInputPortType(0), Is.EqualTo(7));
            Assert.That(node.GetInputPortColor(0), Is.EqualTo(Color.Orange));
            Assert.That(node.GetInputPortIcon(0), Is.SameAs(leftIcon));
            Assert.That(node.GetOutputPortType(0), Is.EqualTo(3));
            Assert.That(node.GetOutputPortColor(0), Is.EqualTo(Color.CornflowerBlue));
            Assert.That(node.GetOutputPortIcon(0), Is.SameAs(rightIcon));
            Assert.That(node.GetInputPortPosition(0), Is.EqualTo(new Vector2(0, 74)));
            Assert.That(node.GetOutputPortPosition(0), Is.EqualTo(new Vector2(120, 74)));
            Assert.That(node.GetInputPortDrawBounds(0), Is.EqualTo(new Rectangle(0, 68, 12, 12)));
            Assert.That(node.GetOutputPortDrawBounds(0), Is.EqualTo(new Rectangle(108, 68, 12, 12)));
            Assert.That(node.GetSlotStyleBoxBounds(2), Is.EqualTo(Rectangle.Empty));

            node.SetSlotTypeLeft(2, 8);
            node.SetSlotColorRight(2, Color.Lime);
            node.SetSlotMetadataLeft(2, "left");
            node.SetSlotMetadataRight(2, 42);
            node.SetSlotDrawStyleBox(2, true);
            node.SetSlotEnabledLeft(2, false);
            Assert.That(node.InputPortCount, Is.EqualTo(0));
            Assert.That(node.OutputPortCount, Is.EqualTo(1));
            Assert.That(node.IsSlotEnabledLeft(2), Is.False);
            Assert.That(node.GetSlotTypeLeft(2), Is.EqualTo(8));
            Assert.That(node.GetSlotColorRight(2), Is.EqualTo(Color.Lime));
            Assert.That(node.GetSlotMetadataLeft(2), Is.EqualTo("left"));
            Assert.That(node.GetSlotMetadataRight(2), Is.EqualTo(42));
            Assert.That(node.IsSlotDrawStyleBox(2), Is.True);
            Assert.That(node.GetSlotStyleBoxBounds(2), Is.EqualTo(new Rectangle(8, 64, 104, 20)));

            node.SetSlotEnabledLeft(1, true);
            node.SetSlotTypeLeft(1, 5);
            Assert.That(node.InputPortCount, Is.EqualTo(1));
            Assert.That(node.GetInputPortSlot(0), Is.EqualTo(1));
            Assert.That(node.GetInputPortType(0), Is.EqualTo(5));
            Assert.That(node.GetInputPortDrawBounds(0), Is.EqualTo(new Rectangle(0, 50, 8, 8)));
            Assert.That(node.GetSlotStyleBoxBounds(1), Is.EqualTo(new Rectangle(8, 44, 104, 20)));

            node.ClearSlot(2);
            Assert.That(node.IsSlotEnabledRight(2), Is.False);
            Assert.That(node.OutputPortCount, Is.EqualTo(0));
            node.ClearAllSlots();
            Assert.That(node.InputPortCount, Is.EqualTo(0));
            Assert.That(node.GetSlotTypeLeft(99), Is.EqualTo(0));
            Assert.That(node.GetSlotColorLeft(99), Is.EqualTo(Color.White));
            Assert.Throws<ArgumentOutOfRangeException>(() => node.SetSlot(-1, true, 0, Color.White, false, 0, Color.White));
            Assert.Throws<InvalidOperationException>(() => node.SetSlotColorLeft(99, Color.Red));
            Assert.That(updated, Does.Contain(2));
            Assert.That(updated, Does.Contain(1));
        }

        [Test]
        public void GraphNode_ArrangesChildBackedSlotsAndTracksTheirFinalCentersLikeGodot()
        {
            var node = new GraphNode { Size = new Vector2(120, 120) };
            var first = new Control { CustomMinimumSize = new Vector2(30, 10) };
            var cappedExpand = new Control
            {
                CustomMinimumSize = new Vector2(40, 20),
                CustomMaximumSize = new Vector2(-1, 40),
                VerticalSizeFlags = SizeFlags.Fill | SizeFlags.Expand
            };
            node.AddChild(first); node.AddChild(cappedExpand);
            node.SetSlot(0, true, 1, Color.Red, false, 0, Color.White);
            node.SetSlot(1, false, 0, Color.White, true, 2, Color.Blue);
            var slotSizeChanges = 0;
            node.SlotSizesChanged += _ => slotSizeChanges++;
            var context = new UIContext(); context.Add(node); context.Layout();

            Assert.That(first.Bounds, Is.EqualTo(new Rectangle(8, 24, 104, 20)));
            Assert.That(cappedExpand.Bounds, Is.EqualTo(new Rectangle(8, 48, 104, 40)), "Godot caps a stretching slot child at its combined maximum and leaves the unused body space undistributed.");
            Assert.That(node.GetInputPortPosition(0), Is.EqualTo(new Vector2(0, 34)));
            Assert.That(node.GetOutputPortPosition(0), Is.EqualTo(new Vector2(120, 68)));
            Assert.That(node.GetSlotStyleBoxBounds(1), Is.EqualTo(new Rectangle(8, 48, 104, 40)));
            Assert.That(slotSizeChanges, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void GraphNode_UsesPanelTitlebarAndSlotThemeMetricsForLayout()
        {
            var theme = new Theme { Separation = 6 };
            theme.SetStyleBox("titlebar", new StyleBoxEmpty { ContentMargin = new Thickness(2, 3, 4, 5) }, nameof(GraphNode));
            theme.SetStyleBox("panel", new StyleBoxEmpty { ContentMargin = new Thickness(7, 8, 9, 10) }, nameof(GraphNode));
            theme.SetStyleBox("slot", new StyleBoxEmpty { ContentMargin = new Thickness(3, 4, 5, 6) }, nameof(GraphNode));
            var node = new GraphNode { CustomMinimumSize = Vector2.Zero, Size = new Vector2(240, 150) };
            var first = new Control { CustomMinimumSize = new Vector2(160, 30) };
            var second = new Control { CustomMinimumSize = new Vector2(120, 30) };
            node.AddChild(first); node.AddChild(second);
            node.SetSlot(0, true, 1, Color.Red, false, 0, Color.White);
            node.SetSlot(1, false, 0, Color.White, true, 2, Color.Blue);
            var context = new UIContext { Theme = theme }; context.Add(node); context.Layout();

            Assert.That(node.GetMinimumSize(), Is.EqualTo(new Vector2(184, 128)));
            Assert.That(first.Bounds, Is.EqualTo(new Rectangle(10, 36, 216, 30)));
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(10, 82, 216, 30)));
            Assert.That(node.GetSlotStyleBoxBounds(0), Is.EqualTo(new Rectangle(7, 32, 224, 40)));
            Assert.That(node.GetInputPortPosition(0), Is.EqualTo(new Vector2(0, 51)));
            Assert.That(node.GetOutputPortPosition(0), Is.EqualTo(new Vector2(240, 97)));
        }

        [Test]
        public void GraphEdit_MapsGodotGraphElementResizeGesturesAndSignals()
        {
            var graph = new GraphEdit { Size = new Vector2(500, 360), MinimapEnabled = false, ShowMenu = false, SnappingDistance = 20 };
            var node = new GraphNode { Name = "Node", Position = new Vector2(40, 40), Size = new Vector2(140, 80), Resizable = true };
            graph.AddChild(node);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var requests = new List<Vector2>();
            var ends = new List<Vector2>();
            node.ResizeRequest += (_, size) => requests.Add(size);
            node.ResizeEnd += (_, size) => ends.Add(size);

            Assert.That(node.IsResizable(), Is.True);
            Assert.That(node.IsResizing, Is.False);
            Assert.That(node.GetResizeHandleBounds(), Is.EqualTo(new Rectangle(168, 108, 12, 12)));

            var handle = new Point(node.Bounds.Right - 2, node.Bounds.Bottom - 2);
            context.Update(Time, Mouse(handle.X, handle.Y), new KeyboardState());
            context.Update(Time, Mouse(handle.X, handle.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(node.IsResizing, Is.True);

            context.Update(Time, Mouse(handle.X + 33, handle.Y + 27, ButtonState.Pressed), new KeyboardState());
            Assert.That(requests[0], Is.EqualTo(new Vector2(173, 107)));
            Assert.That(node.Size, Is.EqualTo(new Vector2(180, 100)), "GraphEdit snaps retained resize requests to the active grid by default.");
            context.Update(Time, Mouse(handle.X + 33, handle.Y + 27), new KeyboardState());
            Assert.That(node.IsResizing, Is.False);
            Assert.That(ends, Is.EqualTo(new[] { new Vector2(180, 100) }));

            context.Layout();
            handle = new Point(node.Bounds.Right - 2, node.Bounds.Bottom - 2);
            context.Update(Time, Mouse(handle.X, handle.Y), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(handle.X, handle.Y, ButtonState.Pressed), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(handle.X + 13, handle.Y + 17, ButtonState.Pressed), new KeyboardState(Keys.LeftControl));
            Assert.That(requests[requests.Count - 1], Is.EqualTo(new Vector2(193, 117)));
            Assert.That(node.Size, Is.EqualTo(new Vector2(193, 117)), "Holding Ctrl follows Godot's snap-inversion policy for graph resize.");
            context.Update(Time, Mouse(handle.X + 13, handle.Y + 17), new KeyboardState(Keys.LeftControl));
            Assert.That(ends[ends.Count - 1], Is.EqualTo(new Vector2(193, 117)));

            node.SetResizable(false);
            Assert.That(node.GetResizeHandleBounds(), Is.EqualTo(Rectangle.Empty));
        }

        [Test]
        public void GraphEdit_ClampsNonAutoshrinkFrameResizeToAttachedElements()
        {
            var graph = new GraphEdit { Size = new Vector2(500, 360), MinimapEnabled = false, ShowMenu = false, SnappingEnabled = false };
            var frame = new GraphFrame { Name = "Frame", AutoshrinkEnabled = false, Position = new Vector2(20, 20), Size = new Vector2(300, 200), Resizable = true };
            var child = new GraphNode { Name = "Child", Position = new Vector2(70, 70), Size = new Vector2(120, 90) };
            graph.AddChild(frame); graph.AddChild(child);
            graph.AttachGraphElementToFrame("Child", "Frame");
            var context = new UIContext(); context.Add(graph); context.Layout();
            var handle = new Point(frame.Bounds.Right - 2, frame.Bounds.Bottom - 2);

            context.Update(Time, Mouse(handle.X, handle.Y), new KeyboardState());
            context.Update(Time, Mouse(handle.X, handle.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(handle.X - 260, handle.Y - 170, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(handle.X - 260, handle.Y - 170), new KeyboardState());

            Assert.That(frame.Size, Is.EqualTo(new Vector2(170, 140)), "Non-autoshrink frames cannot be resized smaller than their attached elements.");
            Assert.That(graph.GetElementFrame("Child"), Is.SameAs(frame));
        }

        [Test]
        public void GraphEdit_MapsGodotGraphElementSelectableRaiseAndDraggedSignals()
        {
            var graph = new GraphEdit { Size = new Vector2(360, 220), MinimapEnabled = false, ShowMenu = false, SnappingEnabled = false };
            var first = new GraphNode { Name = "First", Position = new Vector2(20, 30), Size = new Vector2(90, 60) };
            var second = new GraphNode { Name = "Second", Position = new Vector2(150, 30), Size = new Vector2(90, 60) };
            graph.AddChild(first); graph.AddChild(second);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var elementSelected = 0;
            var elementDeselected = 0;
            var graphSelected = 0;
            var raiseRequests = 0;
            var dragged = new List<(Vector2 From, Vector2 To)>();
            first.NodeSelected += _ => elementSelected++;
            first.NodeDeselected += _ => elementDeselected++;
            first.RaiseRequest += _ => raiseRequests++;
            first.Dragged += (_, from, to) => dragged.Add((from, to));
            graph.NodeSelected += (_, node) => { if (node == first) graphSelected++; };

            Assert.That(first.IsSelectable(), Is.True);
            Assert.That(first.IsSelected(), Is.False);
            Assert.That(first.IsScalingMenus(), Is.False);
            first.SetScalingMenus(true);
            Assert.That(first.IsScalingMenus(), Is.True);

            first.SetSelected(true);
            Assert.That(first.Selected, Is.True);
            Assert.That(elementSelected, Is.EqualTo(1));
            first.SetSelectable(false);
            Assert.That(first.Selected, Is.False);
            Assert.That(elementDeselected, Is.EqualTo(1), "Disabling selectable clears current selection like Godot.");
            graph.SelectNode(first);
            Assert.That(first.Selected, Is.False);
            Assert.That(graphSelected, Is.EqualTo(0));

            first.SetSelectable(true);
            context.Update(Time, Mouse(30, 40), new KeyboardState());
            context.Update(Time, Mouse(30, 40, ButtonState.Pressed), new KeyboardState());
            Assert.That(first.GetDragFrom(), Is.EqualTo(new Vector2(20, 30)));
            Assert.That(raiseRequests, Is.EqualTo(1));
            Assert.That(graphSelected, Is.EqualTo(1));
            Assert.That(first.Selected, Is.True);
            Assert.That(second.Selected, Is.False);

            context.Update(Time, Mouse(65, 60, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(65, 60), new KeyboardState());
            Assert.That(first.Position, Is.EqualTo(new Vector2(55, 50)));
            Assert.That(dragged, Is.EqualTo(new[] { (new Vector2(20, 30), new Vector2(55, 50)) }));
        }

        [Test]
        public void GraphEdit_RaisesAndSelectsNonDraggableGraphElementsWithoutDragging()
        {
            var graph = new GraphEdit { Size = new Vector2(260, 160), MinimapEnabled = false, ShowMenu = false, SnappingEnabled = false };
            var node = new GraphNode { Name = "Node", Position = new Vector2(20, 30), Size = new Vector2(90, 60), Draggable = false };
            graph.AddChild(node);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var raiseRequests = 0;
            var dragged = 0;
            node.RaiseRequest += _ => raiseRequests++;
            node.Dragged += (_, _, _) => dragged++;

            context.Update(Time, Mouse(30, 40), new KeyboardState());
            context.Update(Time, Mouse(30, 40, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(90, 90, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(90, 90), new KeyboardState());

            Assert.That(raiseRequests, Is.EqualTo(1));
            Assert.That(node.Selected, Is.True);
            Assert.That(node.Position, Is.EqualTo(new Vector2(20, 30)));
            Assert.That(dragged, Is.EqualTo(0));
        }

        [Test]
        public void GraphEdit_UsesGodotDirectionalConnectionTypeRegistryForInteractiveTargets()
        {
            var graph = new GraphEdit();
            var source = new GraphNode { Name = "Source" }; source.AddOutputPort("float", 1);
            var target = new GraphNode { Name = "Target" }; target.AddInputPort("int", 2);
            graph.AddChild(source); graph.AddChild(target);

            Assert.That(graph.IsValidConnectionType(1, 2), Is.False);
            Assert.That(graph.IsConnectionTargetValid("Source", 0, "Target", 0), Is.False);
            graph.AddValidConnectionType(1, 2);
            Assert.That(graph.IsValidConnectionType(1, 2), Is.True);
            Assert.That(graph.IsConnectionTargetValid("Source", 0, "Target", 0), Is.True);
            Assert.That(graph.IsConnectionTargetValid("Target", 0, "Source", 0), Is.False);
            graph.RemoveValidConnectionType(1, 2);
            target.IgnoreInvalidConnectionType = true;
            Assert.That(graph.IsConnectionTargetValid("Source", 0, "Target", 0), Is.True);
            Assert.That(graph.ConnectNode("Source", 0, "Target", 0), Is.True);
        }

        [Test]
        public void GraphEdit_DragsOnlyToGodotCompatiblePorts()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 220), MinimapEnabled = false };
            var source = new GraphNode { Name = "Source", Position = new Vector2(20, 20), Size = new Vector2(100, 60) }; source.AddOutputPort("float", 1);
            var target = new GraphNode { Name = "Target", Position = new Vector2(220, 100), Size = new Vector2(100, 60) }; target.AddInputPort("int", 2);
            graph.AddChild(source); graph.AddChild(target);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var output = source.GetOutputPortScreenPosition(0).ToPoint(); var input = target.GetInputPortScreenPosition(0).ToPoint();

            context.Update(Time, Mouse(output.X, output.Y), new KeyboardState());
            context.Update(Time, Mouse(output.X, output.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(input.X, input.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.IsConnectionDragging, Is.True);
            Assert.That(graph.ConnectionDragTarget, Is.Null);
            context.Update(Time, Mouse(input.X, input.Y), new KeyboardState());
            Assert.That(graph.Connections, Is.Empty);

            graph.AddValidConnectionType(1, 2);
            context.Update(Time, Mouse(output.X, output.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(input.X, input.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.ConnectionDragTarget, Is.EqualTo(new GraphConnection("Source", 0, "Target", 0)));
            context.Update(Time, Mouse(input.X, input.Y), new KeyboardState());
            Assert.That(graph.Connections, Is.EqualTo(new[] { new GraphConnection("Source", 0, "Target", 0) }));

            graph.ClearConnections();
            context.Update(Time, Mouse(input.X, input.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(output.X, output.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.ConnectionDragTarget, Is.EqualTo(new GraphConnection("Source", 0, "Target", 0)));
            context.Update(Time, Mouse(output.X, output.Y), new KeyboardState());
            Assert.That(graph.Connections, Is.EqualTo(new[] { new GraphConnection("Source", 0, "Target", 0) }));
        }

        [Test]
        public void GraphEdit_MapsGodotKeyboardConnectionMode()
        {
            var graph = new GraphEdit { Size = new Vector2(460, 260), MinimapEnabled = false };
            var source = new GraphNode { Name = "Source", Position = new Vector2(20, 20), Size = new Vector2(100, 60) }; source.AddOutputPort("float", 1);
            var target = new GraphNode { Name = "Target", Position = new Vector2(220, 40), Size = new Vector2(100, 60) }; target.AddInputPort("int", 2);
            var alternate = new GraphNode { Name = "Alternate", Position = new Vector2(220, 150), Size = new Vector2(100, 60) }; alternate.AddInputPort("float", 1);
            graph.AddChild(source); graph.AddChild(target); graph.AddChild(alternate);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var started = new List<(string Node, int Port, bool Output)>();
            var ended = 0;
            var toEmpty = new List<(string Node, int Port, Vector2 Position)>();
            graph.ConnectionDragStarted += (_, node, port, output) => started.Add((node, port, output));
            graph.ConnectionDragEnded += _ => ended++;
            graph.ConnectionToEmpty += (_, node, port, position) => toEmpty.Add((node, port, position));

            graph.StartKeyboardConnecting(source, -1, 0);
            Assert.That(graph.IsKeyboardConnecting, Is.True);
            Assert.That(graph.IsKeyboardConnectingMode(), Is.True);
            Assert.That(graph.IsConnectionDragging, Is.True);
            Assert.That(started, Is.EqualTo(new[] { ("Source", 0, true) }));
            graph.EndKeyboardConnecting(target, 0, -1);
            Assert.That(graph.Connections, Is.Empty);
            Assert.That(toEmpty, Is.EqualTo(new[] { ("Source", 0, Vector2.Zero) }));
            Assert.That(graph.IsKeyboardConnecting, Is.False);
            Assert.That(graph.IsConnectionDragging, Is.False);
            Assert.That(ended, Is.EqualTo(1));

            graph.AddValidConnectionType(1, 2);
            graph.StartKeyboardConnecting(source, -1, 0);
            graph.EndKeyboardConnecting(target, 0, -1);
            Assert.That(graph.Connections, Is.EqualTo(new[] { new GraphConnection("Source", 0, "Target", 0) }));
            Assert.That(ended, Is.EqualTo(2));

            graph.ClearConnections();
            graph.StartKeyboardConnecting(alternate, 0, -1);
            graph.EndKeyboardConnecting(source, -1, 0);
            Assert.That(graph.Connections, Is.EqualTo(new[] { new GraphConnection("Source", 0, "Alternate", 0) }));
            Assert.That(started[started.Count - 1], Is.EqualTo(("Alternate", 0, false)));
        }

        [Test]
        public void GraphEdit_MapsGodotKeyboardDisconnectReconnect()
        {
            var graph = new GraphEdit { Size = new Vector2(460, 260), MinimapEnabled = false };
            var source = new GraphNode { Name = "Source", Position = new Vector2(20, 20), Size = new Vector2(100, 60) }; source.AddOutputPort("float", 1);
            var target = new GraphNode { Name = "Target", Position = new Vector2(220, 40), Size = new Vector2(100, 60) }; target.AddInputPort("float", 1);
            var alternate = new GraphNode { Name = "Alternate", Position = new Vector2(220, 150), Size = new Vector2(100, 60) }; alternate.AddInputPort("float", 1);
            graph.AddChild(source); graph.AddChild(target); graph.AddChild(alternate);
            graph.ConnectNode("Source", 0, "Target", 0);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var disconnected = new List<GraphConnection>();
            var started = new List<(string Node, int Port, bool Output)>();
            var toEmpty = 0;
            graph.DisconnectionRequest += (_, connection) => disconnected.Add(connection);
            graph.ConnectionDragStarted += (_, node, port, output) => started.Add((node, port, output));
            graph.ConnectionToEmpty += (_, _, _, _) => toEmpty++;

            graph.StartKeyboardConnecting(target, 0, -1);
            Assert.That(graph.IsKeyboardConnecting, Is.True);
            Assert.That(disconnected, Is.EqualTo(new[] { new GraphConnection("Source", 0, "Target", 0) }));
            Assert.That(started, Is.EqualTo(new[] { ("Source", 0, true) }));
            Assert.That(graph.Connections, Is.Empty);
            graph.EndKeyboardConnecting(alternate, 0, -1);
            Assert.That(graph.Connections, Is.EqualTo(new[] { new GraphConnection("Source", 0, "Alternate", 0) }));
            Assert.That(toEmpty, Is.EqualTo(0), "Godot suppresses empty-release signals for just-disconnected keyboard reconnects.");

            graph.AddValidLeftDisconnectType(1);
            graph.StartKeyboardConnecting(source, -1, 0);
            Assert.That(graph.Connections, Is.Empty);
            Assert.That(started[started.Count - 1], Is.EqualTo(("Alternate", 0, false)));
            graph.EndKeyboardConnecting(source, -1, 0);
            Assert.That(graph.Connections, Is.EqualTo(new[] { new GraphConnection("Source", 0, "Alternate", 0) }));
        }

        [Test]
        public void GraphEdit_MapsGodotConnectionDragSignalsAndForceEnd()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 220), MinimapEnabled = false };
            var source = new GraphNode { Name = "Source", Position = new Vector2(20, 20), Size = new Vector2(100, 60) }; source.AddOutputPort("float", 1);
            var target = new GraphNode { Name = "Target", Position = new Vector2(220, 100), Size = new Vector2(100, 60) }; target.AddInputPort("float", 1);
            graph.AddChild(source); graph.AddChild(target);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var output = source.GetOutputPortScreenPosition(0).ToPoint();
            var input = target.GetInputPortScreenPosition(0).ToPoint();
            var started = new List<(string Node, int Port, bool Output)>();
            var ended = 0;
            var toEmpty = new List<(string Node, int Port, Vector2 Position)>();
            var fromEmpty = new List<(string Node, int Port, Vector2 Position)>();
            graph.ConnectionDragStarted += (_, node, port, isOutput) => started.Add((node, port, isOutput));
            graph.ConnectionDragEnded += _ => ended++;
            graph.ConnectionToEmpty += (_, node, port, position) => toEmpty.Add((node, port, position));
            graph.ConnectionFromEmpty += (_, node, port, position) => fromEmpty.Add((node, port, position));

            context.Update(Time, Mouse(output.X, output.Y), new KeyboardState());
            context.Update(Time, Mouse(output.X, output.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(started, Is.EqualTo(new[] { ("Source", 0, true) }));
            context.Update(Time, Mouse(180, 40, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(180, 40), new KeyboardState());
            Assert.That(toEmpty, Is.EqualTo(new[] { ("Source", 0, new Vector2(180, 40)) }));
            Assert.That(fromEmpty, Is.Empty);
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(graph.IsConnectionDragging, Is.False);

            context.Update(Time, Mouse(input.X, input.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(started[started.Count - 1], Is.EqualTo(("Target", 0, false)));
            context.Update(Time, Mouse(160, 160, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(160, 160), new KeyboardState());
            Assert.That(fromEmpty, Is.EqualTo(new[] { ("Target", 0, new Vector2(160, 160)) }));
            Assert.That(ended, Is.EqualTo(2));

            context.Update(Time, Mouse(output.X, output.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(input.X, input.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(input.X, input.Y), new KeyboardState());
            Assert.That(graph.Connections, Is.EqualTo(new[] { new GraphConnection("Source", 0, "Target", 0) }));
            Assert.That(ended, Is.EqualTo(3));

            graph.ClearConnections();
            context.Update(Time, Mouse(output.X, output.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.IsConnectionDragging, Is.True);
            graph.ForceConnectionDragEnd();
            graph.ForceConnectionDragEnd();
            Assert.That(graph.IsConnectionDragging, Is.False);
            Assert.That(ended, Is.EqualTo(4));
        }

        [Test]
        public void GraphEdit_MapsGodotEndpointDisconnectDragAndReconnectGestures()
        {
            var graph = new GraphEdit { Size = new Vector2(460, 240), MinimapEnabled = false };
            var source = new GraphNode { Name = "Source", Position = new Vector2(20, 20), Size = new Vector2(100, 60) }; source.AddOutputPort("float", 1);
            var target = new GraphNode { Name = "Target", Position = new Vector2(220, 40), Size = new Vector2(100, 60) }; target.AddInputPort("float", 1);
            var alternate = new GraphNode { Name = "Alternate", Position = new Vector2(220, 150), Size = new Vector2(100, 60) }; alternate.AddInputPort("float", 1);
            graph.AddChild(source); graph.AddChild(target); graph.AddChild(alternate);
            graph.ConnectNode("Source", 0, "Target", 0);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var targetInput = target.GetInputPortScreenPosition(0).ToPoint();
            var alternateInput = alternate.GetInputPortScreenPosition(0).ToPoint();
            var sourceOutput = source.GetOutputPortScreenPosition(0).ToPoint();
            var disconnected = new List<GraphConnection>();
            var started = new List<(string Node, int Port, bool Output)>();
            var toEmpty = 0;
            graph.DisconnectionRequest += (_, connection) => disconnected.Add(connection);
            graph.ConnectionDragStarted += (_, node, port, isOutput) => started.Add((node, port, isOutput));
            graph.ConnectionToEmpty += (_, _, _, _) => toEmpty++;

            context.Update(Time, Mouse(targetInput.X, targetInput.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(disconnected, Is.EqualTo(new[] { new GraphConnection("Source", 0, "Target", 0) }));
            Assert.That(started, Is.EqualTo(new[] { ("Source", 0, true) }));
            Assert.That(graph.Connections, Is.Empty);
            context.Update(Time, Mouse(170, 80, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(170, 80), new KeyboardState());
            Assert.That(toEmpty, Is.EqualTo(0));
            Assert.That(graph.Connections, Is.Empty);

            graph.ConnectNode("Source", 0, "Target", 0);
            context.Update(Time, Mouse(targetInput.X, targetInput.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(alternateInput.X, alternateInput.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(alternateInput.X, alternateInput.Y), new KeyboardState());
            Assert.That(graph.Connections.Count, Is.EqualTo(1));
            Assert.That(graph.Connections[0].FromNode, Is.EqualTo("Source"));
            Assert.That(graph.Connections[0].FromPort, Is.EqualTo(0));
            Assert.That(graph.Connections[0].ToNode, Is.EqualTo("Alternate"));
            Assert.That(graph.Connections[0].ToPort, Is.EqualTo(0));

            graph.ClearConnections();
            graph.ConnectNode("Source", 0, "Target", 0);
            graph.SetRightDisconnects(false);
            context.Update(Time, Mouse(targetInput.X, targetInput.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.Connections.Count, Is.EqualTo(1));
            Assert.That(graph.Connections[0].FromNode, Is.EqualTo("Source"));
            Assert.That(graph.Connections[0].FromPort, Is.EqualTo(0));
            Assert.That(graph.Connections[0].ToNode, Is.EqualTo("Target"));
            Assert.That(graph.Connections[0].ToPort, Is.EqualTo(0));
            Assert.That(started[started.Count - 1], Is.EqualTo(("Target", 0, false)));
            graph.ForceConnectionDragEnd();
            context.Update(Time, Mouse(targetInput.X, targetInput.Y), new KeyboardState());

            graph.AddValidLeftDisconnectType(1);
            context.Update(Time, Mouse(sourceOutput.X, sourceOutput.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.Connections, Is.Empty);
            Assert.That(started[started.Count - 1], Is.EqualTo(("Target", 0, false)));
            context.Update(Time, Mouse(170, 120, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(170, 120), new KeyboardState());
        }

        [Test]
        public void GraphEdit_MapsGodotBoxSelectionAndFrameContainment()
        {
            var graph = new GraphEdit { Size = new Vector2(360, 240), MinimapEnabled = false, ShowMenu = false };
            var inside = new GraphNode { Name = "Inside", Position = new Vector2(40, 40), Size = new Vector2(60, 40) };
            var partial = new GraphNode { Name = "Partial", Position = new Vector2(160, 60), Size = new Vector2(60, 40) };
            var outside = new GraphNode { Name = "Outside", Position = new Vector2(260, 40), Size = new Vector2(60, 40) };
            var frame = new GraphFrame { Name = "Frame", AutoshrinkEnabled = false, Position = new Vector2(70, 120), Size = new Vector2(80, 45) };
            var partialFrame = new GraphFrame { Name = "PartialFrame", AutoshrinkEnabled = false, Position = new Vector2(170, 140), Size = new Vector2(80, 50) };
            graph.AddChild(inside); graph.AddChild(partial); graph.AddChild(outside); graph.AddChild(frame); graph.AddChild(partialFrame);
            var context = new UIContext(); context.Add(graph); context.Layout();

            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, Mouse(10, 10, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.IsBoxSelecting, Is.True);
            Assert.That(graph.GetSelectedNodes(), Is.Empty);

            context.Update(Time, Mouse(185, 180, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.BoxSelectionRect, Is.EqualTo(new Rectangle(10, 10, 175, 170)));
            Assert.That(graph.GetSelectedNodes(), Is.EqualTo(new GraphNode[] { inside, partial, frame }));
            Assert.That(outside.Selected, Is.False);
            Assert.That(partialFrame.Selected, Is.False, "Godot only selects GraphFrame rows when the box fully encloses the frame.");

            context.Update(Time, Mouse(185, 180), new KeyboardState());
            Assert.That(graph.IsBoxSelecting, Is.False);
            Assert.That(graph.BoxSelectionRect, Is.EqualTo(new Rectangle(10, 10, 175, 170)));
            Assert.That(graph.GetSelectedNodes(), Is.EqualTo(new GraphNode[] { inside, partial, frame }));

            graph.DeselectAll();
            graph.SelectNode(inside);
            graph.SetBoxSelectionEnabled(false);
            context.Update(Time, Mouse(20, 210, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.IsBoxSelecting, Is.False);
            Assert.That(graph.Panner.IsPanning, Is.True);
            context.Update(Time, Mouse(40, 210, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(40, 210), new KeyboardState());
            Assert.That(graph.Panner.IsPanning, Is.False);
            Assert.That(graph.GetSelectedNodes(), Is.EqualTo(new[] { inside }));
        }

        [Test]
        public void GraphEdit_ProvidesGodotBuiltInMinimapViewportAndPanning()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 300) };
            var start = new GraphNode { Name = "Start", Position = Vector2.Zero, Size = new Vector2(100, 60) };
            start.AddOutputPort("out", 1, Color.Red);
            var end = new GraphNode { Name = "End", Position = new Vector2(1000, 400), Size = new Vector2(100, 60) };
            end.AddInputPort("in", 1, Color.Blue);
            graph.AddChild(start); graph.AddChild(end);
            graph.ConnectNode("Start", 0, "End", 0);
            var context = new UIContext(); context.Add(graph); context.Layout();

            Assert.That(graph.Minimap.Graph, Is.SameAs(graph));
            Assert.That(graph.MinimapEnabled, Is.True);
            Assert.That(graph.MinimapSize, Is.EqualTo(new Vector2(240, 160)));
            Assert.That(graph.MinimapOpacity, Is.EqualTo(.65f));
            Assert.That(graph.Minimap.Bounds, Is.EqualTo(new Rectangle(150, 130, 240, 160)));
            Assert.That(graph.Minimap.GetNodeBounds(start), Is.EqualTo(new Rectangle(203, 182, 12, 7)));
            Assert.That(graph.Minimap.GetCameraBounds(), Is.EqualTo(new Rectangle(203, 182, 48, 36)));
            var minimapConnection = graph.Minimap.GetConnectionLinePoints(new GraphConnection("Start", 0, "End", 0));
            Assert.That(minimapConnection.Count, Is.EqualTo(33));
            Assert.That(minimapConnection[0].X, Is.EqualTo(215.5f).Within(.1f));
            Assert.That(minimapConnection[0].Y, Is.EqualTo(186.3f).Within(.1f));
            Assert.That(minimapConnection[minimapConnection.Count - 1].X, Is.EqualTo(324.5f).Within(.1f));
            Assert.That(minimapConnection[minimapConnection.Count - 1].Y, Is.EqualTo(234.7f).Within(.1f));
            graph.SetConnectionActivity("Start", 0, "End", 0, .5f);
            var minimapColors = graph.Minimap.GetConnectionLineColors(new GraphConnection("Start", 0, "End", 0), new Theme { ConnectionActivityColor = Color.Yellow });
            Assert.That(minimapColors.From, Is.EqualTo(Color.Lerp(Color.Red, Color.Yellow, .5f)));
            Assert.That(minimapColors.To, Is.EqualTo(Color.Lerp(Color.Blue, Color.Yellow, .5f)));
            var center = graph.Minimap.Bounds.Center;
            context.Update(Time, Mouse(center.X, center.Y), new KeyboardState());
            context.Update(Time, Mouse(center.X, center.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.ScrollOffset.X, Is.EqualTo(350).Within(1));
            Assert.That(graph.ScrollOffset.Y, Is.EqualTo(80).Within(1));
            var lowerRight = new Point(graph.Minimap.Bounds.Right - 1, graph.Minimap.Bounds.Bottom - 1);
            context.Update(Time, Mouse(lowerRight.X, lowerRight.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.ScrollOffset.X, Is.EqualTo(1333f).Within(1));
            Assert.That(graph.ScrollOffset.Y, Is.EqualTo(732.6f).Within(.1f));
            var outside = new Point(graph.Minimap.Bounds.Right + 20, graph.Minimap.Bounds.Bottom + 20);
            context.Update(Time, Mouse(outside.X, outside.Y, ButtonState.Pressed), new KeyboardState());
            Assert.That(graph.ScrollOffset.X, Is.EqualTo(1506.5f).Within(.1f), "Godot continues converting captured minimap motion outside its bounds without clamping.");
            Assert.That(graph.ScrollOffset.Y, Is.EqualTo(906.1f).Within(.1f));
            context.Update(Time, Mouse(outside.X, outside.Y), new KeyboardState());

            graph.MinimapEnabled = false;
            Assert.That(graph.Minimap.Visible, Is.False);
            graph.MinimapEnabled = true; graph.MinimapSize = new Vector2(120, 80); graph.MinimapOpacity = .3f; context.Layout();
            Assert.That(graph.Minimap.Bounds, Is.EqualTo(new Rectangle(270, 210, 120, 80)));
            Assert.That(graph.MinimapOpacity, Is.EqualTo(.3f));
        }

        [Test]
        public void GraphEditMinimap_PreservesGraphAspectRatioInsideItsPaddedRenderArea()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 300), ShowMenu = false };
            var first = new GraphNode { Position = Vector2.Zero, Size = new Vector2(100, 100) };
            var second = new GraphNode { Position = new Vector2(1000, 100), Size = new Vector2(100, 100), Visible = false };
            graph.AddChild(first); graph.AddChild(second);
            var context = new UIContext(); context.Add(graph); context.Layout();

            Assert.That(graph.Minimap.GetNodeBounds(first), Is.EqualTo(new Rectangle(203, 197, 12, 12)),
                "Godot includes hidden GraphElements in the expanded scroll extent, uniformly fits it, and centers the letterboxed axis inside 5px minimap padding.");
        }

        [Test]
        public void GraphEdit_ResizesItsMinimapFromTheTopLeftHandleLikeGodot()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 300), ShowMenu = false };
            var context = new UIContext(); context.Add(graph); context.Layout();
            var resizeHandle = new Point(graph.Minimap.Bounds.X + 2, graph.Minimap.Bounds.Y + 2);
            Assert.That(graph.Minimap.GetResizeHandleBounds(), Is.EqualTo(new Rectangle(150, 130, 12, 12)));

            context.Update(Time, Mouse(resizeHandle.X, resizeHandle.Y), new KeyboardState());
            context.Update(Time, Mouse(resizeHandle.X, resizeHandle.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(resizeHandle.X - 20, resizeHandle.Y - 20, ButtonState.Pressed), new KeyboardState());
            context.Layout();

            Assert.That(graph.MinimapSize, Is.EqualTo(new Vector2(260, 180)));
            Assert.That(graph.Minimap.Bounds, Is.EqualTo(new Rectangle(130, 110, 260, 180)), "The minimap remains anchored to GraphEdit's lower-right corner while its top-left resize handle moves.");
            context.Update(Time, Mouse(resizeHandle.X - 20, resizeHandle.Y - 20), new KeyboardState());

            graph.MinimapSize = new Vector2(390, 290); context.Layout();
            var cappedHandle = new Point(graph.Minimap.Bounds.X + 2, graph.Minimap.Bounds.Y + 2);
            context.Update(Time, Mouse(cappedHandle.X, cappedHandle.Y), new KeyboardState());
            context.Update(Time, Mouse(cappedHandle.X, cappedHandle.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(cappedHandle.X - 20, cappedHandle.Y - 20, ButtonState.Pressed), new KeyboardState());

            Assert.That(graph.MinimapSize, Is.EqualTo(new Vector2(390, 290)), "Godot caps the minimap at GraphEdit's size minus twice its 5px padding.");
        }

        [Test]
        public void GraphEdit_ProvidesGodotToolbarTogglesAndGridSnapping()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 300), MinimapEnabled = false };
            graph.AddChild(new GraphNode { Name = "Node", Position = new Vector2(200, 120), Size = new Vector2(80, 50) });
            var arranged = 0; graph.NodesArranged += _ => arranged++;
            var context = new UIContext(); context.Add(graph); context.Layout();

            Assert.That(graph.Toolbar.Visible, Is.True);
            Assert.That(graph.GridToggleButton.ButtonPressed, Is.True);
            Assert.That(graph.SnappingToggleButton.ButtonPressed, Is.True);
            Assert.That(graph.SnappingDistanceSpinBox.Value, Is.EqualTo(20));
            Assert.That(graph.MinimapToggleButton.ButtonPressed, Is.False);

            Click(graph.ZoomInButton.Bounds.Center);
            Assert.That(graph.Zoom, Is.EqualTo(1.2f).Within(.001f));
            Assert.That(graph.ZoomLabel.Text, Is.EqualTo("120%"));
            Click(graph.GridToggleButton.Bounds.Center); Click(graph.SnappingToggleButton.Bounds.Center); Click(graph.MinimapToggleButton.Bounds.Center); Click(graph.ArrangeButton.Bounds.Center);
            Assert.That(graph.ShowGrid, Is.False);
            Assert.That(graph.SnappingEnabled, Is.False);
            Assert.That(graph.MinimapEnabled, Is.True);
            Assert.That(arranged, Is.EqualTo(1));

            graph.SnappingDistanceSpinBox.Value = 30;
            Assert.That(graph.SnappingDistance, Is.EqualTo(30));
            graph.SnappingDistance = 20; graph.SnappingDistanceScale = .5f;
            Assert.That(graph.SnapPosition(new Vector2(16, 14)), Is.EqualTo(new Vector2(20, 10)));
            graph.GridPattern = GraphEditGridPattern.Dots;
            Assert.That(graph.GridPattern, Is.EqualTo(GraphEditGridPattern.Dots));

            graph.ShowMenu = false; graph.ShowZoomLabel = true; graph.ShowZoomButtons = false; graph.ShowGridButtons = false; graph.ShowMinimapButton = false; graph.ShowArrangeButton = false;
            Assert.That(graph.Toolbar.Visible, Is.False);
            Assert.That(graph.ZoomLabel.Visible, Is.True);
            Assert.That(graph.ZoomInButton.Visible, Is.False);
            Assert.That(graph.GridToggleButton.Visible, Is.False);
            Assert.That(graph.SnappingDistanceSpinBox.Visible, Is.False);
            Assert.That(graph.MinimapToggleButton.Visible, Is.False);
            Assert.That(graph.ArrangeButton.Visible, Is.False);

            void Click(Point point)
            {
                context.Update(Time, Mouse(point.X, point.Y), new KeyboardState());
                context.Update(Time, Mouse(point.X, point.Y, ButtonState.Pressed), new KeyboardState());
                context.Update(Time, Mouse(point.X, point.Y), new KeyboardState());
            }
        }

        [Test]
        public void GraphEdit_MapsGodotPublicConfigurationAndConnectionListApis()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 300) };
            var source = new GraphNode { Name = "Source" }; source.AddOutputPort("float", 1);
            var target = new GraphNode { Name = "Target" }; target.AddInputPort("int", 2);
            var second = new GraphNode { Name = "Second" }; second.AddInputPort("int", 2);
            graph.AddChild(source); graph.AddChild(target); graph.AddChild(second);

            graph.SetZoomMin(.5f); graph.SetZoomMax(3f); graph.SetZoomStep(1.5f); graph.SetZoom(2f);
            graph.SetScrollOffset(new Vector2(12, 34));
            graph.SetPanningScheme(GraphEditPanningScheme.ScrollPans);
            graph.SetShowGrid(false);
            graph.SetGridPattern(GraphEditGridPattern.Dots);
            graph.SetSnappingEnabled(false);
            graph.SetSnappingDistance(40);
            graph.SetConnectionLinesCurvature(.25f);
            graph.SetConnectionLinesThickness(5.5f);
            graph.SetConnectionLinesAntialiased(false);
            graph.SetMinimapSize(new Vector2(80, 90));
            graph.SetMinimapOpacity(1.5f);
            graph.SetMinimapEnabled(false);
            graph.SetShowMenu(false);
            graph.SetShowZoomLabel(true);
            graph.SetShowZoomButtons(false);
            graph.SetShowGridButtons(false);
            graph.SetShowMinimapButton(false);
            graph.SetShowArrangeButton(false);
            graph.SetRightDisconnects(false);
            graph.SetTypeNames(new Dictionary<int, string> { { 1, "float" }, { 2, null } });
            graph.AddValidRightDisconnectType(1);
            graph.AddValidLeftDisconnectType(2);

            Assert.That(graph.GetScrollOffset(), Is.EqualTo(new Vector2(12, 34)));
            Assert.That(graph.GetZoom(), Is.EqualTo(2f));
            Assert.That(graph.GetZoomMin(), Is.EqualTo(.5f));
            Assert.That(graph.GetZoomMax(), Is.EqualTo(3f));
            Assert.That(graph.GetZoomStep(), Is.EqualTo(1.5f));
            Assert.That(graph.GetPanningScheme(), Is.EqualTo(GraphEditPanningScheme.ScrollPans));
            Assert.That(graph.IsShowingGrid(), Is.False);
            Assert.That(graph.GetGridPattern(), Is.EqualTo(GraphEditGridPattern.Dots));
            Assert.That(graph.IsSnappingEnabled(), Is.False);
            Assert.That(graph.GetSnappingDistance(), Is.EqualTo(40));
            Assert.That(graph.GetConnectionLinesCurvature(), Is.EqualTo(.25f));
            Assert.That(graph.GetConnectionLinesThickness(), Is.EqualTo(5.5f));
            Assert.That(graph.IsConnectionLinesAntialiased(), Is.False);
            Assert.That(graph.GetMinimapSize(), Is.EqualTo(new Vector2(80, 90)));
            Assert.That(graph.GetMinimapOpacity(), Is.EqualTo(1f));
            Assert.That(graph.IsMinimapEnabled(), Is.False);
            Assert.That(graph.IsShowingMenu(), Is.False);
            Assert.That(graph.IsShowingZoomLabel(), Is.True);
            Assert.That(graph.IsShowingZoomButtons(), Is.False);
            Assert.That(graph.IsShowingGridButtons(), Is.False);
            Assert.That(graph.IsShowingMinimapButton(), Is.False);
            Assert.That(graph.IsShowingArrangeButton(), Is.False);
            Assert.That(graph.IsRightDisconnectsEnabled(), Is.False);
            Assert.That(graph.GetMenuHBox(), Is.SameAs(graph.ToolbarButtons));
            Assert.That(graph.GetTypeNames()[1], Is.EqualTo("float"));
            Assert.That(graph.GetTypeNames()[2], Is.EqualTo(string.Empty));
            Assert.That(graph.IsValidRightDisconnectType(1), Is.True);
            Assert.That(graph.IsValidLeftDisconnectType(2), Is.True);

            graph.SetConnections(new[] {
                new GraphConnection("Source", 0, "Target", 0),
                new GraphConnection("Source", 0, "Target", 0),
                new GraphConnection("Source", 0, "Second", 0),
                new GraphConnection("Source", 0, "Missing", 0),
            });
            graph.SetConnectionActivity("Source", 0, "Target", 0, .75f);

            Assert.That(graph.GetConnectionList(), Is.EqualTo(new[] { new GraphConnection("Source", 0, "Target", 0), new GraphConnection("Source", 0, "Second", 0) }));
            Assert.That(graph.IsNodeConnected("Source", 0, "Target", 0), Is.True);
            Assert.That(graph.GetConnectionCount("Source", 0), Is.EqualTo(2));
            Assert.That(graph.GetConnectionListFromNode("Target"), Is.EqualTo(new[] { new GraphConnection("Source", 0, "Target", 0) }));
            Assert.That(graph.GetConnectionActivity("Source", 0, "Target", 0), Is.EqualTo(.75f));

            graph.RemoveValidRightDisconnectType(1);
            graph.RemoveValidLeftDisconnectType(2);
            graph.ClearConnections();

            Assert.That(graph.IsValidRightDisconnectType(1), Is.False);
            Assert.That(graph.IsValidLeftDisconnectType(2), Is.False);
            Assert.That(graph.GetConnectionList(), Is.Empty);
            Assert.That(graph.GetConnectionActivity("Source", 0, "Target", 0), Is.EqualTo(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.SetPanningScheme((GraphEditPanningScheme)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.SetGridPattern((GraphEditGridPattern)99));
        }

        [Test]
        public void GraphEdit_EmitsGodotClipboardDuplicateAndDeleteRequestsWhileFocused()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 300), MinimapEnabled = false, ShowMenu = false };
            var selected = new GraphNode { Name = "Selected", Size = new Vector2(100, 60) };
            var unselected = new GraphNode { Name = "Unselected", Position = new Vector2(120, 0), Size = new Vector2(100, 60) };
            graph.AddChild(selected); graph.AddChild(unselected); selected.SetSelected(true);
            var requests = new List<string>();
            graph.CopyNodesRequest += _ => requests.Add("copy");
            graph.CutNodesRequest += _ => requests.Add("cut");
            graph.PasteNodesRequest += _ => requests.Add("paste");
            graph.DuplicateNodesRequest += _ => requests.Add("duplicate");
            graph.DeleteNodesRequest += (_, names) => requests.Add("delete:" + string.Join(",", names));
            var context = new UIContext(); context.Add(graph); context.Layout(); graph.GrabFocus();

            PressShortcut(Keys.C, Keys.LeftControl);
            PressShortcut(Keys.X, Keys.LeftControl);
            PressShortcut(Keys.V, Keys.LeftControl);
            PressShortcut(Keys.D, Keys.LeftControl);
            PressShortcut(Keys.Delete);

            Assert.That(requests, Is.EqualTo(new[] { "copy", "cut", "paste", "duplicate", "delete:Selected" }));

            var outside = new Control { Size = new Vector2(10, 10), FocusMode = FocusMode.All };
            context.Add(outside); outside.GrabFocus();
            PressShortcut(Keys.C, Keys.LeftControl);
            Assert.That(requests.Count, Is.EqualTo(5), "GraphEdit request shortcuts only apply while it or one of its descendants owns focus.");

            void PressShortcut(Keys key, params Keys[] modifiers)
            {
                var pressed = new Keys[modifiers.Length + 1];
                Array.Copy(modifiers, pressed, modifiers.Length); pressed[pressed.Length - 1] = key;
                context.Update(Time, Mouse(0, 0), new KeyboardState(pressed));
                context.Update(Time, Mouse(0, 0), new KeyboardState());
            }
        }

        [Test]
        public void GraphEdit_UsesGodotConnectionLineGeometryThicknessAndActivityTint()
        {
            var graph = new GraphEdit { Size = new Vector2(400, 300), MinimapEnabled = false };
            var source = new GraphNode { Name = "Source", Position = new Vector2(20, 40), Size = new Vector2(100, 60) };
            source.AddOutputPort("float", 1, Color.Red);
            var target = new GraphNode { Name = "Target", Position = new Vector2(220, 80), Size = new Vector2(100, 60) };
            target.AddInputPort("float", 1, Color.Blue);
            graph.AddChild(source); graph.AddChild(target);
            graph.ConnectNode("Source", 0, "Target", 0);
            var connection = new GraphConnection("Source", 0, "Target", 0);

            var curved = graph.GetConnectionLinePoints(new Vector2(10, 20), new Vector2(110, 60));
            Assert.That(curved.Count, Is.EqualTo(33), "Antialiased retained curves use a denser deterministic tessellation.");
            Assert.That(curved[0], Is.EqualTo(new Vector2(10, 20)));
            Assert.That(curved[curved.Count - 1], Is.EqualTo(new Vector2(110, 60)));
            Assert.That(curved[1].X, Is.GreaterThan(10));
            Assert.That(curved[1].Y, Is.GreaterThan(20));

            graph.SetConnectionLinesAntialiased(false);
            Assert.That(graph.GetConnectionLinePoints(new Vector2(10, 20), new Vector2(110, 60)).Count, Is.EqualTo(21));
            graph.SetConnectionLinesCurvature(0);
            Assert.That(graph.GetConnectionLinePoints(new Vector2(10, 20), new Vector2(110, 60)), Is.EqualTo(new[] { new Vector2(10, 20), new Vector2(110, 60) }));

            graph.SetConnectionLinesThickness(0);
            Assert.That(graph.GetConnectionLinesThickness(), Is.EqualTo(0f));
            graph.SetConnectionLinesThickness(5.5f);
            Assert.That(graph.GetConnectionLinesThickness(), Is.EqualTo(5.5f));

            var theme = new Theme { ConnectionActivityColor = Color.Yellow };
            var colors = graph.GetConnectionLineColors(connection, theme);
            Assert.That(colors.From, Is.EqualTo(Color.Red));
            Assert.That(colors.To, Is.EqualTo(Color.Blue));

            graph.SetConnectionActivity("Source", 0, "Target", 0, .5f);
            colors = graph.GetConnectionLineColors(connection, theme);
            Assert.That(colors.From, Is.EqualTo(Color.Lerp(Color.Red, Color.Yellow, .5f)));
            Assert.That(colors.To, Is.EqualTo(Color.Lerp(Color.Blue, Color.Yellow, .5f)));
        }

        [Test]
        public void GraphEdit_MapsGodotConnectionHitAndRectQueries()
        {
            var graph = new GraphEdit { Size = new Vector2(420, 260), MinimapEnabled = false, ConnectionLinesCurvature = 0 };
            var source = new GraphNode { Name = "Source", Position = new Vector2(20, 40), Size = new Vector2(100, 80) };
            source.AddOutputPort("float", 1);
            var top = new GraphNode { Name = "Top", Position = new Vector2(240, 30), Size = new Vector2(100, 60) };
            top.AddInputPort("float", 1);
            var bottom = new GraphNode { Name = "Bottom", Position = new Vector2(240, 150), Size = new Vector2(100, 60) };
            bottom.AddInputPort("float", 1);
            graph.AddChild(source); graph.AddChild(top); graph.AddChild(bottom);
            graph.ConnectNode("Source", 0, "Top", 0);
            graph.ConnectNode("Source", 0, "Bottom", 0);
            graph.ConnectNode("Source", 0, "Top", 99);
            var context = new UIContext(); context.Add(graph); context.Layout();

            var topLine = graph.GetConnectionLine(source.GetOutputPortScreenPosition(0), top.GetInputPortScreenPosition(0));
            Assert.That(topLine, Is.EqualTo(new[] { source.GetOutputPortScreenPosition(0), top.GetInputPortScreenPosition(0) }));

            var topMidpoint = (source.GetOutputPortScreenPosition(0) + top.GetInputPortScreenPosition(0)) * .5f;
            var bottomMidpoint = (source.GetOutputPortScreenPosition(0) + bottom.GetInputPortScreenPosition(0)) * .5f;
            Assert.That(graph.GetClosestConnectionAtPoint(topMidpoint + new Vector2(0, 2)), Is.EqualTo(new GraphConnection("Source", 0, "Top", 0)));
            Assert.That(graph.GetClosestConnectionAtPoint(new Vector2(5, 240), 3), Is.Null);

            var hits = graph.GetConnectionsIntersectingWithRect(new RectangleF(bottomMidpoint.X - 6, bottomMidpoint.Y - 6, 12, 12));
            Assert.That(hits, Is.EqualTo(new[] { new GraphConnection("Source", 0, "Bottom", 0) }));
            Assert.That(graph.GetConnectionsIntersectingWithRect(new RectangleF(4, 220, 20, 20)), Is.Empty);
        }

        [Test]
        public void GraphEdit_AttachesAndAutoshrinksGodotGraphFrames()
        {
            var graph = new GraphEdit();
            var frame = new GraphFrame { Name = "Group", AutoshrinkEnabled = true, AutoshrinkMargin = 10 };
            var first = new GraphNode { Name = "First", Position = new Vector2(20, 30), Size = new Vector2(100, 50) };
            var second = new GraphNode { Name = "Second", Position = new Vector2(150, 80), Size = new Vector2(40, 20) };
            graph.AddChild(frame); graph.AddChild(first); graph.AddChild(second);
            graph.AttachGraphElementToFrame("First", "Group"); graph.AttachGraphElementToFrame("Second", "Group");
            frame.Position += new Vector2(5, 7);
            var groupedPosition = frame.Position; var groupedSize = frame.Size;
            graph.DetachGraphElementFromFrame("First");

            Assert.That(groupedPosition, Is.EqualTo(new Vector2(15, 13)));
            Assert.That(groupedSize, Is.EqualTo(new Vector2(190, 104)));
            Assert.That(first.Position, Is.EqualTo(new Vector2(25, 37)));
            Assert.That(second.Position, Is.EqualTo(new Vector2(155, 87)));
            Assert.That(frame.Position, Is.EqualTo(new Vector2(145, 63)));
            Assert.That(frame.Size, Is.EqualTo(new Vector2(60, 54)));
            Assert.That(graph.GetElementFrame("First"), Is.Null);
            Assert.That(graph.GetAttachedNodesOfFrame("Group"), Is.EqualTo(new[] { "Second" }));
        }

        [Test]
        public void GraphFrame_MapsGodotPublicTitlebarAutoshrinkAndTintState()
        {
            var graph = new GraphEdit();
            var frame = new GraphFrame { Name = "Group", Position = new Vector2(10, 10), Size = new Vector2(220, 120) };
            var child = new GraphNode { Name = "Child", Position = new Vector2(60, 70), Size = new Vector2(80, 40) };
            graph.AddChild(frame); graph.AddChild(child);
            graph.AttachGraphElementToFrame("Child", "Group");
            var changed = new List<Vector2>();
            frame.AutoshrinkChanged += (_, size) => changed.Add(size);

            Assert.That(frame.GetTitlebarHBox(), Is.Not.Null);
            Assert.That(frame.GetTitlebarHBox().Name, Is.EqualTo("_titlebar_hbox"));
            frame.SetTitle("Frame title");
            Assert.That(frame.GetTitle(), Is.EqualTo("Frame title"));
            Assert.That(frame.GetTitlebarSize(), Is.EqualTo(new Vector2(0, 24)));
            Assert.That(frame.IsAutoshrinkEnabled(), Is.True);
            Assert.That(frame.GetAutoshrinkMargin(), Is.EqualTo(40));
            Assert.That(frame.GetDragMargin(), Is.EqualTo(16));
            Assert.That(frame.IsTintColorEnabled(), Is.False);
            Assert.That(frame.GetTintColor(), Is.EqualTo(new Color(77, 77, 77, 191)));

            frame.SetTintColorEnabled(true);
            frame.SetTintColor(Color.Purple);
            frame.SetDragMargin(24);
            Assert.That(frame.IsTintColorEnabled(), Is.True);
            Assert.That(frame.GetTintColor(), Is.EqualTo(Color.Purple));
            Assert.That(frame.GetDragMargin(), Is.EqualTo(24));

            frame.SetAutoshrinkMargin(8);
            Assert.That(frame.GetAutoshrinkMargin(), Is.EqualTo(8));
            Assert.That(changed.Count, Is.EqualTo(1));
            Assert.That(frame.Position, Is.EqualTo(new Vector2(52, 46)));
            Assert.That(frame.Size, Is.EqualTo(new Vector2(96, 72)));

            frame.SetAutoshrinkEnabled(false);
            Assert.That(frame.IsAutoshrinkEnabled(), Is.False);
            Assert.That(changed.Count, Is.EqualTo(2));
            Assert.That(frame.Size.X, Is.GreaterThanOrEqualTo(96));
        }

        [Test]
        public void GraphFrame_HitTestingUsesTitlebarAndDragMarginInsteadOfTheBody()
        {
            var graph = new GraphEdit { Size = new Vector2(500, 320), MinimapEnabled = false, ShowMenu = false, SnappingEnabled = false };
            var frame = new GraphFrame
            {
                Name = "Frame",
                AutoshrinkEnabled = false,
                Position = new Vector2(40, 40),
                Size = new Vector2(240, 160),
                DragMargin = 12,
                Resizable = true
            };
            graph.AddChild(frame);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var titlebarPoint = new Point(frame.Bounds.Center.X, frame.Bounds.Y + 8);
            var marginPoint = new Point(frame.Bounds.X + 6, frame.Bounds.Center.Y);
            var bodyPoint = frame.Bounds.Center;

            Assert.That(context.HitTest(titlebarPoint), Is.SameAs(frame));
            Assert.That(context.HitTest(marginPoint), Is.SameAs(frame));
            Assert.That(context.HitTest(bodyPoint), Is.SameAs(graph), "Godot lets pointer input pass through the center of a GraphFrame.");

            context.Update(Time, Mouse(bodyPoint.X, bodyPoint.Y), new KeyboardState());
            context.Update(Time, Mouse(bodyPoint.X, bodyPoint.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(bodyPoint.X + 30, bodyPoint.Y + 20, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(bodyPoint.X + 30, bodyPoint.Y + 20), new KeyboardState());
            Assert.That(frame.Position, Is.EqualTo(new Vector2(40, 40)));

            context.Update(Time, Mouse(marginPoint.X, marginPoint.Y), new KeyboardState());
            context.Update(Time, Mouse(marginPoint.X, marginPoint.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(marginPoint.X + 30, marginPoint.Y + 20, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(marginPoint.X + 30, marginPoint.Y + 20), new KeyboardState());
            Assert.That(frame.Position, Is.EqualTo(new Vector2(70, 60)));

            frame.SetAutoshrinkEnabled(true);
            context.Layout();
            var resizeRequests = 0;
            frame.ResizeRequest += (_, _) => resizeRequests++;
            var resizePoint = new Point(frame.Bounds.Right - 2, frame.Bounds.Bottom - 2);
            var originalSize = frame.Size;
            context.Update(Time, Mouse(resizePoint.X, resizePoint.Y), new KeyboardState());
            context.Update(Time, Mouse(resizePoint.X, resizePoint.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(resizePoint.X + 30, resizePoint.Y + 20, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(resizePoint.X + 30, resizePoint.Y + 20), new KeyboardState());
            Assert.That(frame.Size, Is.EqualTo(originalSize));
            Assert.That(resizeRequests, Is.Zero, "Godot suppresses GraphFrame resize requests while autoshrink is enabled.");
        }

        [Test]
        public void GraphFrame_ArrangesTitlebarAndBodyChildrenUsingFrameStyleMargins()
        {
            var theme = new Theme();
            theme.SetStyleBox("titlebar", new StyleBoxEmpty { ContentMargin = new Thickness(3, 4, 5, 6) }, nameof(GraphFrame));
            theme.SetStyleBox("panel", new StyleBoxEmpty { ContentMargin = new Thickness(7, 8, 9, 10) }, nameof(GraphFrame));
            var frame = new GraphFrame { Size = new Vector2(200, 140) };
            var titleControl = new Control { CustomMinimumSize = new Vector2(60, 18) };
            var bodyControl = new Control
            {
                CustomMinimumSize = new Vector2(80, 30),
                HorizontalSizeFlags = SizeFlags.ShrinkCenter,
                VerticalSizeFlags = SizeFlags.ShrinkEnd
            };
            frame.GetTitlebarHBox().AddChild(titleControl);
            frame.AddChild(bodyControl);
            var context = new UIContext { Theme = theme }; context.Add(frame); context.Layout();

            Assert.That(frame.GetMinimumSize(), Is.EqualTo(new Vector2(96, 76)));
            Assert.That(frame.GetTitlebarSize(), Is.EqualTo(new Vector2(68, 28)));
            Assert.That(frame.GetTitlebarHBox().Position, Is.EqualTo(new Vector2(3, 4)));
            Assert.That(frame.GetTitlebarHBox().Size, Is.EqualTo(new Vector2(192, 18)));
            Assert.That(bodyControl.Position, Is.EqualTo(new Vector2(59, 100)));
            Assert.That(bodyControl.Size, Is.EqualTo(new Vector2(80, 30)));
        }

        [Test]
        public void GraphEdit_RequestsApplicationOwnedFrameLinkingAfterNodeDrop()
        {
            var graph = new GraphEdit { Size = new Vector2(520, 320), MinimapEnabled = false, ShowMenu = false, SnappingEnabled = false };
            var frame = new GraphFrame { Name = "Frame", AutoshrinkEnabled = false, Position = new Vector2(260, 40), Size = new Vector2(200, 180) };
            var node = new GraphNode { Name = "Node", Position = new Vector2(30, 80), Size = new Vector2(100, 60) };
            graph.AddChild(frame); graph.AddChild(node);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var requests = new List<(IReadOnlyList<string> Elements, string Frame)>();
            graph.GraphElementsLinkedToFrameRequest += (_, elements, targetFrame) => requests.Add((elements, targetFrame));
            var start = node.Bounds.Center;
            var drop = frame.Bounds.Center;

            context.Update(Time, Mouse(start.X, start.Y), new KeyboardState());
            context.Update(Time, Mouse(start.X, start.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(drop.X, drop.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(drop.X, drop.Y), new KeyboardState());

            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests[0].Elements, Is.EqualTo(new[] { "Node" }));
            Assert.That(requests[0].Frame, Is.EqualTo("Frame"));
            Assert.That(graph.GetElementFrame("Node"), Is.Null, "Godot delegates attachment mutation to the application receiving the request.");
        }

        [Test]
        public void GraphEdit_DragsTheSelectedGroupAndRequestsPluralFrameLinking()
        {
            var graph = new GraphEdit { Size = new Vector2(560, 360), MinimapEnabled = false, ShowMenu = false, SnappingEnabled = false };
            var frame = new GraphFrame { Name = "Frame", AutoshrinkEnabled = false, Position = new Vector2(300, 30), Size = new Vector2(220, 220) };
            var first = new GraphNode { Name = "First", Position = new Vector2(20, 60), Size = new Vector2(100, 60) };
            var second = new GraphNode { Name = "Second", Position = new Vector2(20, 160), Size = new Vector2(100, 60) };
            graph.AddChild(frame); graph.AddChild(first); graph.AddChild(second);
            first.SetSelected(true); second.SetSelected(true);
            var context = new UIContext(); context.Add(graph); context.Layout();
            var requests = new List<(IReadOnlyList<string> Elements, string Frame)>();
            graph.GraphElementsLinkedToFrameRequest += (_, elements, targetFrame) => requests.Add((elements, targetFrame));
            var start = first.Bounds.Center;
            var drop = frame.Bounds.Center;
            var delta = new Vector2(drop.X - start.X, drop.Y - start.Y);

            context.Update(Time, Mouse(start.X, start.Y), new KeyboardState());
            context.Update(Time, Mouse(start.X, start.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(drop.X, drop.Y, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(drop.X, drop.Y), new KeyboardState());

            Assert.That(first.Position, Is.EqualTo(new Vector2(20, 60) + delta));
            Assert.That(second.Position, Is.EqualTo(new Vector2(20, 160) + delta));
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests[0].Elements, Is.EquivalentTo(new[] { "First", "Second" }));
            Assert.That(requests[0].Frame, Is.EqualTo("Frame"));
            Assert.That(graph.GetElementFrame("First"), Is.Null);
            Assert.That(graph.GetElementFrame("Second"), Is.Null);
        }

        [Test]
        public void CodeEdit_ProvidesLineCountingAndTextEditing()
        {
            var code = new CodeEdit { Text = "line one\nline two" };
            code.InsertCodeText("// ");

            Assert.That(code.LineCount, Is.EqualTo(2));
            Assert.That(code.Text, Is.EqualTo("// line one\nline two"));
        }

        [Test]
        public void CodeEdit_ImplementsGodotIndentBraceAndDebuggerGutterState()
        {
            var code = new CodeEdit { Text = "if ready:", AutoIndentEnabled = true, IndentUsingSpaces = true, IndentSize = 2, DrawBreakpointsGutter = true, DrawBookmarksGutter = true, DrawExecutingLinesGutter = true, LineNumbersZeroPadded = true, LineNumbersMinDigits = 3 };
            code.SetAutoIndentPrefixes(new[] { ":" });
            code.SetCaret(0, code.GetLine(0).Length);
            code.InsertCodeText("\n");
            code.InsertText("(");
            code.InsertText(")");
            code.SetLineAsBreakpoint(0, true); code.SetLineAsBookmarked(1, true); code.SetLineAsExecuting(1, true);
            code.Select(0, code.Text.Length); code.IndentLines(); code.UnindentLines();

            Assert.That(code.Text, Is.EqualTo("if ready:\n  ()"));
            Assert.That(code.HasAutoBraceCompletionOpenKey("("), Is.True);
            Assert.That(code.HasAutoBraceCompletionCloseKey(")"), Is.True);
            Assert.That(code.GetAutoBraceCompletionCloseKey("("), Is.EqualTo(")"));
            Assert.That(code.IndentText, Is.EqualTo("  "));
            Assert.That(code.GetBreakpointedLines(), Is.EqualTo(new[] { 0 }));
            Assert.That(code.GetBookmarkedLines(), Is.EqualTo(new[] { 1 }));
            Assert.That(code.GetExecutingLines(), Is.EqualTo(new[] { 1 }));
            Assert.That(code.IsLineBreakpointed(0), Is.True);
            Assert.That(code.IsLineBookmarked(1), Is.True);
            Assert.That(code.IsLineExecuting(1), Is.True);
        }

        [Test]
        public void CodeEdit_MapsGodotIndentationLineFoldingAndVisibleViewport()
        {
            var code = new CodeEdit { Text = "func ready():\n    first()\n    if nested:\n        leaf()\n    final()\ntail()", Size = new Vector2(160, 32), LineFoldingEnabled = true, DrawFoldGutter = true };
            var foldChanges = 0; code.FoldStateChanged += _ => foldChanges++;

            Assert.That(code.IsLineFoldingEnabled(), Is.True);
            Assert.That(code.IsDrawingFoldGutter(), Is.True);
            Assert.That(code.CanFoldLine(0), Is.True);
            Assert.That(code.CanFoldLine(1), Is.False);
            code.SetCaret(3, 1);
            code.FoldLine(0);

            Assert.That(code.IsLineFolded(0), Is.True);
            Assert.That(code.GetFoldedLines(), Is.EqualTo(new[] { 0 }));
            Assert.That(code.GetFoldedLineHeader(3), Is.EqualTo(0));
            Assert.That(code.CaretLine, Is.Zero, "Folding collapses a caret that was in the hidden block onto its header.");
            Assert.That(code.GetTotalVisibleLineCount(), Is.EqualTo(2));
            Assert.That(code.GetVisibleLineCountInRange(0, 4), Is.EqualTo(1));
            Assert.That(code.IsLineInViewport(3), Is.False);
            code.KeyPressed(Keys.Down);
            Assert.That(code.CaretLine, Is.EqualTo(5), "Caret navigation skips folded lines.");

            code.UnfoldLine(3);
            Assert.That(code.GetFoldedLines(), Is.Empty);
            Assert.That(code.GetTotalVisibleLineCount(), Is.EqualTo(6));
            Assert.That(code.GetVisibleLineCountInRange(0, 4), Is.EqualTo(5));
            code.FoldAllLines();
            Assert.That(code.IsLineFolded(0), Is.True);
            code.UnfoldAllLines();
            Assert.That(code.GetFoldedLines(), Is.Empty);
            Assert.That(foldChanges, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void CodeEdit_MapsGodotExplicitCodeRegionFolding()
        {
            var code = new CodeEdit { Text = "#region Outer\nplain text\n#region Inner\nmore text\n#endregion\nfinal text\n#endregion\ntail", LineFoldingEnabled = true };
            code.AddCommentDelimiter("#", lineOnly: true);

            Assert.That(code.GetCodeRegionStartTag(), Is.EqualTo("region"));
            Assert.That(code.GetCodeRegionEndTag(), Is.EqualTo("endregion"));
            Assert.That(code.IsLineCodeRegionStart(0), Is.True);
            Assert.That(code.IsLineCodeRegionStart(2), Is.True);
            Assert.That(code.IsLineCodeRegionEnd(4), Is.True);
            Assert.That(code.IsLineCodeRegionEnd(6), Is.True);
            Assert.That(code.CanFoldLine(0), Is.True);
            code.FoldLine(0);
            Assert.That(code.GetFoldedLines(), Is.EqualTo(new[] { 0 }));
            Assert.That(code.GetTotalVisibleLineCount(), Is.EqualTo(2));
            Assert.That(code.GetFoldedLineHeader(4), Is.EqualTo(0));
            code.UnfoldLine(4);
            Assert.That(code.GetTotalVisibleLineCount(), Is.EqualTo(8));

            code.SetCodeRegionTags("section", "endsection");
            code.SetLine(0, "#section Outer"); code.SetLine(6, "#endsection");
            Assert.That(code.IsLineCodeRegionStart(0), Is.True);
            Assert.That(code.IsLineCodeRegionEnd(6), Is.True);

            var created = new CodeEdit { Text = "first\nsecond", LineFoldingEnabled = true };
            created.AddCommentDelimiter("#", lineOnly: true); created.Select(0, created.Text.Length); created.CreateCodeRegion();
            Assert.That(created.LineCount, Is.EqualTo(4));
            Assert.That(created.IsLineCodeRegionStart(0), Is.True);
            Assert.That(created.IsLineCodeRegionEnd(3), Is.True);
            Assert.That(created.IsLineFolded(0), Is.True);
        }

        [Test]
        public void CodeEdit_MapsGodotConfiguredMultilineDelimiterFolding()
        {
            var code = new CodeEdit { Text = "/* comment starts\ncomment body\n*/\n\"\"\" string starts\nstring body\n\"\"\"\ntail", LineFoldingEnabled = true };
            code.AddCommentDelimiter("/*", "*/");
            code.AddStringDelimiter("\"\"\"", "\"\"\"");

            Assert.That(code.HasCommentDelimiter("/*"), Is.True);
            Assert.That(code.HasStringDelimiter("\"\"\""), Is.True);
            Assert.That(code.CanFoldLine(0), Is.True);
            code.FoldLine(0);
            Assert.That(code.GetFoldedLines(), Is.EqualTo(new[] { 0 }));
            Assert.That(code.GetTotalVisibleLineCount(), Is.EqualTo(5));
            code.UnfoldLine(1);
            Assert.That(code.CanFoldLine(3), Is.True);
            code.FoldLine(3);
            Assert.That(code.GetFoldedLineHeader(5), Is.EqualTo(3));
            Assert.That(code.GetTotalVisibleLineCount(), Is.EqualTo(5));
            code.UnfoldAllLines();
            Assert.That(code.GetTotalVisibleLineCount(), Is.EqualTo(7));
            code.RemoveStringDelimiter("\"\"\""); code.RemoveCommentDelimiter("/*");
            Assert.That(code.HasStringDelimiter("\"\"\""), Is.False);
            Assert.That(code.HasCommentDelimiter("/*"), Is.False);
        }

        [Test]
        public void CodeEdit_MapsGodotRetainedCodeCompletionRequestSelectionAndConfirmation()
        {
            var code = new CodeEdit { Text = "pri suffix" };
            code.SetCaret(0, 3); code.SetCodeCompletionPrefixes(new[] { "." });
            var requests = 0;
            code.CodeCompletionRequested += (edit, forced) =>
            {
                requests++;
                Assert.That(forced, Is.True);
                edit.AddCodeCompletionOption(CodeCompletionKind.Function, "print(value)", "print");
                edit.AddCodeCompletionOption(CodeCompletionKind.Function, "private_value", "private_value");
                edit.AddCodeCompletionOption(CodeCompletionKind.Variable, "other", "other");
            };
            code.RequestCodeCompletion(true);

            Assert.That(requests, Is.EqualTo(1));
            Assert.That(code.IsCodeCompletionActive, Is.True);
            Assert.That(code.GetTextForCodeCompletion(), Is.EqualTo("pri\uffff suffix"));
            Assert.That(code.CodeCompletionOptions, Has.Count.EqualTo(3), "A forced request presents every provider candidate.");
            Assert.That(code.GetCodeCompletionOption(0).InsertText, Is.EqualTo("print"));
            code.KeyPressed(Keys.Down);
            Assert.That(code.GetCodeCompletionSelectedIndex(), Is.EqualTo(1));
            code.KeyPressed(Keys.Up);
            code.ConfirmCodeCompletion(replace: true);
            Assert.That(code.Text, Is.EqualTo("print suffix"));
            Assert.That(code.CaretColumn, Is.EqualTo(5));
            Assert.That(code.IsCodeCompletionActive, Is.False);

            code.AddCodeCompletionOption(CodeCompletionKind.Variable, "suffix", "suffix"); code.UpdateCodeCompletionOptions(forced: true);
            Assert.That(code.IsCodeCompletionActive, Is.True);
            code.CancelCodeCompletion();
            Assert.That(code.GetCodeCompletionSelectedIndex(), Is.EqualTo(-1));
            code.SetCodeCompletionEnabled(false); code.RequestCodeCompletion(true);
            Assert.That(requests, Is.EqualTo(1), "Disabled completion does not request candidates.");
        }

        [Test]
        public void CodeEdit_FuzzyFiltersAndRanksCompletionCandidatesLikeGodot()
        {
            var edit = new CodeEdit { Text = "prln" };
            edit.SetCaret(0, 4);
            edit.AddCodeCompletionOption(CodeCompletionKind.Function, "PrintLongName", "PrintLongName()", location: 2);
            edit.AddCodeCompletionOption(CodeCompletionKind.Function, "PrefixLine", "PrefixLine()", location: 1);
            edit.AddCodeCompletionOption(CodeCompletionKind.Function, "Parse", "Parse()", location: 0);

            edit.UpdateCodeCompletionOptions();

            Assert.That(edit.CodeCompletionOptions.Count, Is.EqualTo(2));
            Assert.That(edit.CodeCompletionOptions[0].DisplayText, Is.EqualTo("PrefixLine"));
            Assert.That(edit.CodeCompletionOptions[0].MatchSegments, Is.EqualTo(new[] { new Point(0, 2), new Point(6, 1), new Point(8, 1) }));
            Assert.That(edit.CodeCompletionOptions[1].DisplayText, Is.EqualTo("PrintLongName"));
            Assert.That(edit.CodeCompletionOptions[1].MatchSegments, Is.EqualTo(new[] { new Point(0, 2), new Point(5, 1), new Point(7, 1) }));
        }

        [Test]
        public void CodeEdit_MapsGodotCaretRelativeCodeHints()
        {
            var code = new CodeEdit { Text = "first\nsecond", Size = new Vector2(180, 60) };
            code.SetCaret(1, 2); code.SetCodeHint("print(value: Variant)\nReturns void");
            var above = code.GetCodeHintBounds();
            Assert.That(above.IsEmpty, Is.False);
            Assert.That(above.Bottom, Is.LessThanOrEqualTo(60));
            Assert.That(code.CodeHintDrawBelow, Is.False);

            code.SetCodeHintDrawBelow(true);
            var below = code.GetCodeHintBounds();
            Assert.That(code.CodeHintDrawBelow, Is.True);
            Assert.That(below.Top, Is.GreaterThanOrEqualTo(above.Top));
            code.SetCodeHint(string.Empty);
            Assert.That(code.GetCodeHintBounds(), Is.EqualTo(Rectangle.Empty));
        }

        [Test]
        public void CodeEdit_MapsGodotLineManipulationCommands()
        {
            var code = new CodeEdit { Text = "one\ntwo\nthree\nfour" };
            code.SetCaret(1, 0); code.MoveLinesUp();
            Assert.That(code.Text, Is.EqualTo("two\none\nthree\nfour"));
            code.MoveLinesDown();
            Assert.That(code.Text, Is.EqualTo("one\ntwo\nthree\nfour"));

            code.Select(4, 13); code.MoveLinesDown();
            Assert.That(code.Text, Is.EqualTo("one\nfour\ntwo\nthree"));
            code.DeleteLines();
            Assert.That(code.Text, Is.EqualTo("one\nfour"));
            code.SetCaret(0, 0); code.JoinLines(" + ");
            Assert.That(code.Text, Is.EqualTo("one + four"));
            code.DuplicateLines();
            Assert.That(code.Text, Is.EqualTo("one + four\none + four"));

            code.Select(0, 3); code.DuplicateSelection();
            Assert.That(code.Text, Is.EqualTo("oneone + four\none + four"));
            Assert.That(code.SelectedText, Is.EqualTo("one"));

            code = new CodeEdit { Text = "alpha  \n   beta\nomega" };
            code.SetCaret(0, 0); code.JoinLines();
            Assert.That(code.Text, Is.EqualTo("alpha beta\nomega"), "Single-caret joins trim both line boundaries before inserting the separator, like Godot.");
        }

        [Test]
        public void CodeEdit_MapsGodotMultiCaretLineManipulationCommands()
        {
            var code = new CodeEdit { Text = "one\ntwo\nthree\nfour\nfive", MultipleCaretsEnabled = true };
            Assert.That(code.AddCaret(3, 0), Is.EqualTo(1));
            code.SetCaret(1, 0);

            code.MoveLinesUp();
            Assert.That(code.Text, Is.EqualTo("two\none\nfour\nthree\nfive"));
            Assert.That(code.CaretCount, Is.EqualTo(2));
            Assert.That(code.GetCaretLine(), Is.EqualTo(0));
            Assert.That(code.GetCaretLine(1), Is.EqualTo(2));

            code.MoveLinesDown();
            Assert.That(code.Text, Is.EqualTo("one\ntwo\nthree\nfour\nfive"));
            Assert.That(code.GetCaretLine(), Is.EqualTo(1));
            Assert.That(code.GetCaretLine(1), Is.EqualTo(3));

            code = new CodeEdit { Text = "one\ntwo\nthree\nfour\nfive", MultipleCaretsEnabled = true };
            code.SetCaret(0, 0); code.AddCaret(2, 0);
            code.DeleteLines();
            Assert.That(code.Text, Is.EqualTo("two\nfour\nfive"));
            Assert.That(code.CaretCount, Is.EqualTo(2));
            Assert.That(code.GetCaretLine(), Is.EqualTo(0));
            Assert.That(code.GetCaretLine(1), Is.EqualTo(2));
            Assert.That(code.HasSelection, Is.False);
            Assert.That(code.HasCaretSelection(1), Is.False);

            code = new CodeEdit { Text = "one\ntwo\nthree\nfour", MultipleCaretsEnabled = true };
            code.SetCaret(0, 0); code.AddCaret(2, 0);
            code.DuplicateLines();
            Assert.That(code.Text, Is.EqualTo("one\none\ntwo\nthree\nthree\nfour"));
            Assert.That(code.CaretCount, Is.EqualTo(2));
            Assert.That(code.GetCaretLine(), Is.EqualTo(1));
            Assert.That(code.GetCaretLine(1), Is.EqualTo(3));
        }

        [Test]
        public void CodeEdit_MapsGodotMultiCaretJoinAndDuplicateSelectionCommands()
        {
            var code = new CodeEdit { Text = "a  \n  b\nc \n d\ne", MultipleCaretsEnabled = true };
            code.SetCaret(0, 0); code.AddCaret(2, 0);
            code.JoinLines(" ");
            Assert.That(code.Text, Is.EqualTo("a b\nc d\ne"));
            Assert.That(code.CaretCount, Is.EqualTo(2));
            Assert.That(code.GetCaretLine(), Is.EqualTo(0));
            Assert.That(code.GetCaretLine(1), Is.EqualTo(1));
            Assert.That(code.HasSelection, Is.False);

            code = new CodeEdit { Text = "alpha\nbravo\ncharlie", MultipleCaretsEnabled = true };
            code.Select(0, 1, 0, 4);
            code.AddCaret(1, 2); code.Select(1, 0, 1, 2, 1);
            code.DuplicateSelection();
            Assert.That(code.Text, Is.EqualTo("alphlpha\nbrbravo\ncharlie"));
            Assert.That(code.CaretCount, Is.EqualTo(2));
            Assert.That(code.SelectedText, Is.EqualTo("lph"));
            Assert.That(code.Text.Substring(code.GetSelectionFrom(1), code.GetSelectionTo(1) - code.GetSelectionFrom(1)), Is.EqualTo("br"));
        }

        [Test]
        public void TextEdit_MapsGodotMultiCaretClipboardAndShortcutHooks()
        {
            var edit = new TextEdit { Text = "alpha\nbravo", MultipleCaretsEnabled = true };
            using var clipboardContext = new UIContext(new TestClipboard()); clipboardContext.Add(edit);
            edit.Select(0, 0, 0, 1); edit.AddCaret(1, 1); edit.Select(1, 0, 1, 1, 1);
            var copied = string.Empty; edit.CopyRequested += (_, text) => copied = text;
            edit.Copy();
            Assert.That(copied, Is.EqualTo("a\nb"));
            edit.Cut();
            Assert.That(edit.Text, Is.EqualTo("lpha\nravo"));
            Assert.That(edit.CaretCount, Is.EqualTo(2));

            edit = new TextEdit { Text = "a0\nb1", MultipleCaretsEnabled = true };
            edit.SetCaret(0, 1); edit.AddCaret(1, 1);
            edit.Paste("X\nY");
            Assert.That(edit.Text, Is.EqualTo("aX0\nbY1"));
            Assert.That(edit.GetCaretColumn(), Is.EqualTo(2));
            Assert.That(edit.GetCaretColumn(1), Is.EqualTo(2));

            copied = string.Empty;
            edit = new TextEdit { Text = "one\ntwo", MultipleCaretsEnabled = true };
            clipboardContext.Add(edit);
            edit.SetCaret(0, 0); edit.AddCaret(1, 0); edit.CopyRequested += (_, text) => copied = text;
            edit.Copy();
            Assert.That(copied, Is.EqualTo("one\ntwo\n"), "No-selection copy uses merged complete line ranges.");

            edit = new TextEdit { Text = "value", ClipboardTextProvider = _ => "X" };
            var context = new UIContext(); context.Add(edit); context.SetFocus(edit);
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.V));
            Assert.That(edit.Text, Is.EqualTo("Xvalue"));
            edit.SetShortcutKeysEnabled(false);
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.V));
            Assert.That(edit.Text, Is.EqualTo("Xvalue"));
        }

        [Test]
        public void TextEdit_MapsGodotProgrammaticTextManipulationAtCarets()
        {
            var edit = new TextEdit { Text = "one\ntwo", MultipleCaretsEnabled = true };
            edit.SetCaret(0, 1); edit.AddCaret(1, 1);
            edit.InsertTextAtCaret("X", 1);
            Assert.That(edit.Text, Is.EqualTo("one\ntXwo"));
            Assert.That(edit.GetCaretColumn(), Is.EqualTo(1));
            Assert.That(edit.GetCaretColumn(1), Is.EqualTo(2));

            edit = new TextEdit { Text = "ab\ncd", MultipleCaretsEnabled = true };
            edit.Select(0, 1, 0, 2); edit.AddCaret(1, 1); edit.Select(1, 0, 1, 1, 1);
            edit.InsertText("!", 0, 1);
            Assert.That(edit.Text, Is.EqualTo("a!b\ncd"));
            Assert.That(edit.SelectedText, Is.EqualTo("b"));
            Assert.That(edit.GetSelectedText(1), Is.EqualTo("c"));
            edit.RemoveText(0, 0, 0, 2);
            Assert.That(edit.Text, Is.EqualTo("b\ncd"));
            Assert.That(edit.SelectedText, Is.EqualTo("b"));
            Assert.That(edit.GetSelectedText(1), Is.EqualTo("c"));
            edit.Deselect(1);
            Assert.That(edit.HasSelection, Is.True);
            Assert.That(edit.HasCaretSelection(1), Is.False);
            edit.DeleteSelection(-1);
            Assert.That(edit.Text, Is.EqualTo("\ncd"));

            edit = new TextEdit { Text = " \tvalue\n   " };
            Assert.That(edit.GetIndentLevel(0), Is.EqualTo(5));
            Assert.That(edit.GetFirstNonWhitespaceColumn(0), Is.EqualTo(2));
            Assert.That(edit.GetFirstNonWhitespaceColumn(1), Is.EqualTo(3));
        }

        [Test]
        public void TextEdit_MapsGodotSwapLinesWithCaretAndMetadataRetention()
        {
            var edit = new TextEdit { Text = "first\nsecond\nthird", MultipleCaretsEnabled = true };
            edit.AddGutter(); edit.SetLineGutterText(0, 0, "top"); edit.SetLineGutterText(2, 0, "bottom");
            edit.SetLineBackgroundColor(0, Color.Red); edit.SetLineBackgroundColor(2, Color.Blue);
            edit.Select(0, 0, 0, 3); edit.AddCaret(2, 1);
            edit.SwapLines(0, 2);

            Assert.That(edit.Text, Is.EqualTo("third\nsecond\nfirst"));
            Assert.That(edit.GetCaretLine(), Is.EqualTo(2));
            Assert.That(edit.GetCaretColumn(), Is.EqualTo(3));
            Assert.That(edit.GetCaretLine(1), Is.EqualTo(0));
            Assert.That(edit.GetCaretColumn(1), Is.EqualTo(1));
            Assert.That(edit.SelectedText, Is.EqualTo("fir"));
            Assert.That(edit.GetLineGutterText(0, 0), Is.EqualTo("bottom"));
            Assert.That(edit.GetLineGutterText(2, 0), Is.EqualTo("top"));
            Assert.That(edit.GetLineBackgroundColor(0), Is.EqualTo(Color.Blue));
            Assert.That(edit.GetLineBackgroundColor(2), Is.EqualTo(Color.Red));
        }

        [Test]
        public void TextEdit_MapsGodotActionGroupedUndoAndVersions()
        {
            var edit = new TextEdit();
            edit.ClearUndoHistory();
            Assert.That(edit.GetVersion(), Is.EqualTo(0));
            edit.StartAction(TextEditEditAction.Typing);
            Assert.That(edit.GetCurrentAction(), Is.EqualTo(TextEditEditAction.Typing));
            edit.InsertText("a"); edit.InsertText("b");
            Assert.That(edit.HasUndo, Is.False, "An open action does not publish partial undo steps.");
            edit.EndAction();
            Assert.That(edit.Text, Is.EqualTo("ab"));
            Assert.That(edit.HasUndo, Is.True);
            Assert.That(edit.GetVersion(), Is.EqualTo(1));
            edit.TagSavedVersion();
            Assert.That(edit.GetSavedVersion(), Is.EqualTo(1));

            edit.InsertText("c");
            Assert.That(edit.GetVersion(), Is.EqualTo(2));
            edit.Undo();
            Assert.That(edit.Text, Is.EqualTo("ab"));
            Assert.That(edit.GetVersion(), Is.EqualTo(1));
            Assert.That(edit.GetSavedVersion(), Is.EqualTo(1));
            edit.Redo();
            Assert.That(edit.Text, Is.EqualTo("abc"));
            Assert.That(edit.GetVersion(), Is.EqualTo(2));

            edit.StartAction(TextEditEditAction.Backspace); edit.InsertText("d");
            edit.StartAction(TextEditEditAction.Delete);
            Assert.That(edit.GetCurrentAction(), Is.EqualTo(TextEditEditAction.Delete));
            edit.EndAction(); edit.Undo();
            Assert.That(edit.Text, Is.EqualTo("abc"), "Changing action commits the previous action as one undo entry.");
        }

        [Test]
        public void TextEdit_MapsGodotWordAndNextOccurrenceSelections()
        {
            var edit = new TextEdit { Text = "cat dog cat dog cat", MultipleCaretsEnabled = true };
            edit.SetCaret(0, 1);
            edit.SelectWordUnderCaret();
            Assert.That(edit.SelectedText, Is.EqualTo("cat"));
            edit.AddSelectionForNextOccurrence();
            Assert.That(edit.CaretCount, Is.EqualTo(2));
            Assert.That(edit.GetSelectedText(1), Is.EqualTo("cat"));
            Assert.That(edit.GetCaretColumn(1), Is.EqualTo(11));
            edit.AddSelectionForNextOccurrence();
            Assert.That(edit.CaretCount, Is.EqualTo(3));
            Assert.That(edit.GetSelectedText(2), Is.EqualTo("cat"));
            edit.SelectWordUnderCaret(2);
            Assert.That(edit.HasCaretSelection(2), Is.False, "Selecting an already-selected word toggles its selection off.");

            edit = new TextEdit { Text = "cat cat cat", MultipleCaretsEnabled = true };
            edit.SetCaret(0, 1);
            edit.SkipSelectionForNextOccurrence();
            Assert.That(edit.CaretCount, Is.EqualTo(1));
            Assert.That(edit.SelectedText, Is.EqualTo("cat"));
            Assert.That(edit.GetCaretColumn(), Is.EqualTo(7));
            edit.AddSelectionForNextOccurrence();
            Assert.That(edit.CaretCount, Is.EqualTo(2));
            Assert.That(edit.GetSelectedText(1), Is.EqualTo("cat"));
        }

        [Test]
        public void TextEdit_MapsGodotRetainedContextMenuCommands()
        {
            var edit = new TextEdit { Text = "value", Size = new Vector2(160, 48) };
            var copied = string.Empty; edit.CopyRequested += (_, text) => copied = text;
            var clipboard = new TestClipboard();
            var context = new UIContext { Clipboard = clipboard }; context.Add(edit);
            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, right: ButtonState.Pressed), new KeyboardState());
            var menu = edit.GetMenu();
            Assert.That(edit.IsMenuVisible(), Is.True);
            Assert.That(menu.Items, Has.Count.EqualTo(14));
            Assert.That(menu.Items[0].Text, Is.EqualTo("Cut"));
            Assert.That(menu.Items[7].Text, Is.EqualTo("Undo"));
            Assert.That(menu.Items[7].Disabled, Is.False, "Initial direct text assignment is undoable in the retained editor.");

            edit.MenuOption(TextEditMenuOption.SelectAll);
            Assert.That(edit.SelectedText, Is.EqualTo("value"));
            edit.MenuOption(TextEditMenuOption.Copy);
            Assert.That(copied, Is.EqualTo("value"));
            Assert.That(clipboard.Text, Is.EqualTo("value"));
            edit.MenuOption(TextEditMenuOption.Clear);
            Assert.That(edit.Text, Is.Empty);
            edit.MenuOption(TextEditMenuOption.Undo);
            Assert.That(edit.Text, Is.EqualTo("value"));
            clipboard.Text = "paste";
            edit.SetCaret(0, 0); edit.MenuOption(TextEditMenuOption.Paste);
            Assert.That(edit.Text, Is.EqualTo("pastevalue"));

            edit.ContextMenuEnabled = false;
            menu.Hide();
            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, right: ButtonState.Pressed), new KeyboardState());
            Assert.That(edit.IsMenuVisible(), Is.False);
        }

        [TestCase(typeof(TextEdit))]
        [TestCase(typeof(CodeEdit))]
        public void MultilineEditorContextMenuCommandsRetainPartialSelection(Type editorType)
        {
            var edit = (TextEdit)Activator.CreateInstance(editorType)!;
            edit.Font = CreateTestFont();
            edit.Text = "value";
            edit.Size = new Vector2(240, 64);
            edit.Select(0, 1, 0, 4);
            var clipboard = new TestClipboard();
            using var context = new UIContext { Clipboard = clipboard };
            context.Add(edit);
            context.SetFocus(edit);

            void OpenMenu()
            {
                context.Update(Time, Mouse(100, 16), new KeyboardState());
                context.Update(Time, Mouse(100, 16, right: ButtonState.Pressed), new KeyboardState());
                context.Update(Time, Mouse(100, 16), new KeyboardState());
            }

            void ActivateMenuItem(int index)
            {
                var menu = edit.GetMenu();
                var x = menu.Bounds.X + 8;
                var y = menu.Bounds.Y + 1 + (int)(menu.ItemHeight * (index + 0.5f));
                context.Update(Time, Mouse(x, y, ButtonState.Pressed), new KeyboardState());
                context.Update(Time, Mouse(x, y), new KeyboardState());
            }

            OpenMenu();
            ActivateMenuItem(1);
            Assert.That(clipboard.Text, Is.EqualTo("alu"), "Copy should use the retained partial selection.");

            OpenMenu();
            ActivateMenuItem(0);
            Assert.That(clipboard.Text, Is.EqualTo("alu"), "Cut should copy only the retained partial selection.");
            Assert.That(edit.Text, Is.EqualTo("ve"));

            edit.Select(0, 0, 0, 1);
            clipboard.Text = "X";
            OpenMenu();
            ActivateMenuItem(2);
            Assert.That(edit.Text, Is.EqualTo("Xe"), "Paste should replace the retained partial selection.");
        }

        [Test]
        public void TextEdit_MapsGodotDirectionControlCharacterAndStructuredTextState()
        {
            var edit = new TextEdit { Text = "ab\ncd", Size = new Vector2(180, 72) };

            Assert.That(edit.GetTextDirection(), Is.EqualTo(TextDirection.Auto));
            Assert.That(edit.GetLanguage(), Is.Empty);
            Assert.That(edit.GetDrawControlChars(), Is.False);
            Assert.That(edit.GetStructuredTextBidiOverride(), Is.EqualTo(StructuredTextParser.Default));

            edit.SetTextDirection(TextDirection.RightToLeft);
            edit.SetLanguage("he");
            edit.SetDrawControlChars(true);
            edit.SetStructuredTextBidiOverride(StructuredTextParser.Uri);
            edit.SetStructuredTextBidiOverrideOptions(new object[] { "scheme", "host" });

            Assert.That(edit.GetTextDirection(), Is.EqualTo(TextDirection.RightToLeft));
            Assert.That(edit.GetLanguage(), Is.EqualTo("he"));
            Assert.That(edit.GetDrawControlChars(), Is.True);
            Assert.That(edit.GetStructuredTextBidiOverride(), Is.EqualTo(StructuredTextParser.Uri));
            Assert.That(edit.GetStructuredTextBidiOverrideOptions(), Is.EqualTo(new object[] { "scheme", "host" }));

            var context = new UIContext(); context.Add(edit);
            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, right: ButtonState.Pressed), new KeyboardState());

            var menu = edit.GetMenu();
            Assert.That(menu.Items[10].Text, Is.EqualTo("Text Writing Direction"));
            Assert.That(menu.Items[12].Text, Is.EqualTo("Display Control Characters"));
            Assert.That(menu.Items[12].Checked, Is.True);
            Assert.That(menu.Items[13].Text, Is.EqualTo("Insert Control Character"));
            Assert.That(menu.Items[13].Disabled, Is.False);

            var directionMenu = edit.GetTextDirectionMenu();
            Assert.That(directionMenu.Items, Has.Count.EqualTo(4));
            Assert.That(directionMenu.Items[3].Checked, Is.True);

            edit.MenuOption(TextEditMenuOption.DirectionLeftToRight);
            Assert.That(edit.GetTextDirection(), Is.EqualTo(TextDirection.LeftToRight));
            edit.MenuOption(TextEditMenuOption.DisplayControlCharacters);
            Assert.That(edit.GetDrawControlChars(), Is.False);

            edit.SetCaret(0, 1);
            edit.MenuOption(TextEditMenuOption.InsertLeftToRightMark);
            edit.MenuOption(TextEditMenuOption.InsertSoftHyphen);

            Assert.That(edit.Text, Is.EqualTo("a\u200E\u00ADb\ncd"));
            Assert.That(edit.GetCaretColumn(), Is.EqualTo(3));

            var controlMenu = edit.GetControlCharacterMenu();
            Assert.That(controlMenu.Items[0].Text, Does.Contain("LRM"));
            Assert.That(controlMenu.Items[8].Text, Does.Contain("ALM"));
            Assert.That(controlMenu.Items[17].Text, Does.Contain("SHY"));

            edit.Editable = false;
            menu.Hide();
            context.Update(Time, Mouse(8, 8), new KeyboardState());
            context.Update(Time, Mouse(8, 8, right: ButtonState.Pressed), new KeyboardState());

            Assert.That(edit.GetMenu().Items[13].Disabled, Is.True);
        }

        [Test]
        public void CodeEdit_MapsGodotLineLengthGuidelines()
        {
            var code = new CodeEdit { Size = new Vector2(180, 48) };
            code.SetLineLengthGuidelines(new[] { 8, 12, 0, -1, 8 });

            Assert.That(code.GetLineLengthGuidelines(), Is.EqualTo(new[] { 8, 12, 8 }));
            Assert.That(code.GetLineLengthGuidelineX(8), Is.GreaterThan(code.Bounds.Left));
            Assert.That(code.GetLineLengthGuidelineX(12) - code.GetLineLengthGuidelineX(8), Is.EqualTo(32), "Four fallback glyph columns separate the two guidelines.");
            code.SetLineLengthGuidelines(null);
            Assert.That(code.LineLengthGuidelines, Is.Empty);
        }

        [Test]
        public void CodeEdit_MapsGodotSymbolLookupTextWordAndCommandClickPolicy()
        {
            var code = new CodeEdit { Text = "load_asset" };
            code.SetCaret(0, 4);
            Assert.That(code.GetTextWithCursorChar(0, 4), Is.EqualTo("load\uffff_asset"));
            Assert.That(code.GetTextForSymbolLookup(), Is.EqualTo("load\uffff_asset"));
            Assert.That(code.GetLookupWord(0, 4), Is.EqualTo("load_asset"));
            Assert.That(code.GetLookupWord(-1, 0), Is.Empty);

            var requested = string.Empty; code.SymbolLookupRequested += (edit, word, _, _) => { requested = word; edit.SetSymbolLookupWordAsValid(true); };
            code.SetSymbolLookupOnClickEnabled(true);
            var context = new UIContext(); context.Add(code);
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            code.PointerPressed(Point.Zero);
            Assert.That(requested, Is.EqualTo("load_asset"));
            Assert.That(code.SymbolLookupWord, Is.EqualTo("load_asset"));
            code.SetSymbolLookupOnClickEnabled(false);
            Assert.That(code.SymbolLookupWord, Is.Empty);
        }

        [Test]
        public void TextEdit_MapsGodotSyntaxHighlighterProviderAndCodeColorRegions()
        {
            var highlighter = new CodeHighlighter { NumberColor = Color.Orange, MemberVariableColor = Color.LimeGreen };
            highlighter.AddKeywordColor("func", Color.CornflowerBlue);
            highlighter.AddMemberKeywordColor("value", Color.LimeGreen);
            highlighter.AddColorRegion("#", string.Empty, Color.Gray, lineOnly: true);
            var edit = new TextEdit { Text = "func node.value() # comment" };

            edit.SetSyntaxHighlighter(highlighter);
            Assert.That(edit.GetSyntaxHighlighter(), Is.SameAs(highlighter));
            Assert.That(highlighter.TextEdit, Is.SameAs(edit));
            var highlighted = edit.GetLineSyntaxHighlighting(0);
            Assert.That(highlighted, Has.Some.Matches<SyntaxHighlightSpan>(span => span.StartColumn == 0 && span.Length == 4 && span.Color == Color.CornflowerBlue));
            Assert.That(highlighted, Has.Some.Matches<SyntaxHighlightSpan>(span => span.StartColumn == 10 && span.Length == 5 && span.Color == Color.LimeGreen));
            Assert.That(highlighted, Has.Some.Matches<SyntaxHighlightSpan>(span => span.StartColumn == 18 && span.Length == 9 && span.Color == Color.Gray));

            edit.Text = "var total = 42";
            Assert.That(edit.GetLineSyntaxHighlighting(0), Has.Some.Matches<SyntaxHighlightSpan>(span => span.StartColumn == 12 && span.Length == 2 && span.Color == Color.Orange), "Document edits invalidate provider output.");
            edit.SetSyntaxHighlighter(null);
            Assert.That(highlighter.TextEdit, Is.Null);
            Assert.That(edit.GetLineSyntaxHighlighting(0), Is.Empty);
        }

        [Test]
        public void CodeHighlighter_MapsGodotBulkRegionsScalarColorsAndMemberRules()
        {
            var highlighter = new CodeHighlighter();
            highlighter.SetKeywordColors(new Dictionary<string, Color> { ["const"] = Color.CornflowerBlue });
            highlighter.SetMemberKeywordColors(new Dictionary<string, Color> { ["member"] = Color.Orange });
            highlighter.SetMemberVariableColor(Color.LimeGreen);
            highlighter.SetFunctionColor(Color.Violet);
            highlighter.SetNumberColor(Color.Red);
            highlighter.SetSymbolColor(Color.Gray);
            highlighter.SetColorRegions(new[]
            {
                new CodeHighlightColorRegion("/", string.Empty, Color.Pink),
                new CodeHighlightColorRegion("//", string.Empty, Color.SlateGray),
            });
            var edit = new TextEdit { Text = "const member obj.member call (42u) // comment" };
            edit.SetSyntaxHighlighter(highlighter);

            Assert.That(highlighter.GetKeywordColor("const"), Is.EqualTo(Color.CornflowerBlue));
            Assert.That(highlighter.GetMemberKeywordColor("member"), Is.EqualTo(Color.Orange));
            Assert.That(highlighter.GetMemberVariableColor(), Is.EqualTo(Color.LimeGreen));
            Assert.That(highlighter.GetFunctionColor(), Is.EqualTo(Color.Violet));
            Assert.That(highlighter.GetNumberColor(), Is.EqualTo(Color.Red));
            Assert.That(highlighter.GetSymbolColor(), Is.EqualTo(Color.Gray));
            Assert.That(highlighter.GetColorRegions()[0].StartKey, Is.EqualTo("//"), "Longer delimiters take precedence over their prefixes.");
            Assert.That(highlighter.GetColorRegions()[0].LineOnly, Is.True);

            var spans = edit.GetLineSyntaxHighlighting(0);
            Assert.That(spans, Has.Some.Matches<SyntaxHighlightSpan>(span => span.StartColumn == 6 && span.Length == 6 && span.Color == Color.Orange));
            Assert.That(spans, Has.Some.Matches<SyntaxHighlightSpan>(span => span.StartColumn == 17 && span.Length == 6 && span.Color == Color.LimeGreen));
            Assert.That(spans, Has.Some.Matches<SyntaxHighlightSpan>(span => span.StartColumn == 24 && span.Length == 4 && span.Color == Color.Violet));
            Assert.That(spans, Has.None.Matches<SyntaxHighlightSpan>(span => span.StartColumn == 30 && span.Color == Color.Red), "Unsigned suffixes require explicit enablement.");
            Assert.That(spans, Has.Some.Matches<SyntaxHighlightSpan>(span => span.StartColumn == 35 && span.Color == Color.SlateGray));

            highlighter.SetUIntSuffixEnabled(true);
            Assert.That(highlighter.IsUIntSuffixEnabled(), Is.True);
            Assert.That(edit.GetLineSyntaxHighlighting(0), Has.Some.Matches<SyntaxHighlightSpan>(span => span.StartColumn == 30 && span.Length == 3 && span.Color == Color.Red));
            Assert.Throws<ArgumentException>(() => highlighter.AddColorRegion("//", string.Empty, Color.White));
            Assert.Throws<ArgumentException>(() => highlighter.AddColorRegion("word", string.Empty, Color.White));
        }

        [Test]
        public void TextEdit_MapsGodotBoundaryWrappingLayoutQueries()
        {
            var edit = new TextEdit { Text = "alpha beta gamma", WrapAtColumn = 8, Size = new Vector2(120, 32) };
            edit.SetLineWrappingMode(TextEditLineWrappingMode.Boundary);

            Assert.That(edit.WrapMode, Is.True);
            Assert.That(edit.GetLineWrappingMode(), Is.EqualTo(TextEditLineWrappingMode.Boundary));
            Assert.That(edit.IsLineWrapped(0), Is.True);
            Assert.That(edit.GetLineWrapCount(0), Is.EqualTo(2));
            Assert.That(edit.GetLineWrappedText(0), Is.EqualTo(new[] { "alpha ", "beta ", "gamma" }));
            Assert.That(edit.GetLineWrapIndexAtColumn(0, 0), Is.EqualTo(0));
            Assert.That(edit.GetLineWrapIndexAtColumn(0, 6), Is.EqualTo(1));
            Assert.That(edit.GetLineWrapIndexAtColumn(0, 11), Is.EqualTo(2));
            Assert.That(edit.GetLineWidth(0, 2), Is.EqualTo(40));

            edit.SetLineAsFirstVisible(0, 1);
            Assert.That(edit.FirstVisibleLineWrapIndex, Is.EqualTo(1));
            Assert.That(edit.GetScrollPosForLine(0, 2), Is.EqualTo(2));
            Assert.That(edit.GetLastFullVisibleLine(), Is.Zero);
            Assert.That(edit.GetLastFullVisibleLineWrapIndex(), Is.EqualTo(2));
            edit.SetCaret(0, 0); edit.AdjustViewportToCaret();
            Assert.That(edit.FirstVisibleLineWrapIndex, Is.Zero, "Caret reveal can scroll to a preceding wrapped row.");
            edit.SetCaret(0, 11); edit.AdjustViewportToCaret();
            Assert.That(edit.FirstVisibleLineWrapIndex, Is.EqualTo(1), "Caret reveal can scroll to a trailing wrapped row.");
            edit.SetLineAsCenterVisible(0, 2);
            Assert.That(edit.FirstVisibleLineWrapIndex, Is.EqualTo(1));

            edit.WrapMode = false;
            Assert.That(edit.IsLineWrapped(0), Is.False);
            Assert.That(edit.GetLineWrapCount(0), Is.Zero);
            Assert.That(edit.GetLineWrappedText(0), Is.EqualTo(new[] { "alpha beta gamma" }));
        }

        [Test]
        public void TextEditWrapsShapedGraphemesAndInvalidatesOnlyEditedLineLayouts()
        {
            using var latin = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabic = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var edit = new TextEdit
            {
                UIFont = new DynamicUIFont(latin, 18, UIFontHinting.Default, arabic),
                Text = "stable first\nA😀e\u0301 مرحبا shaped middle line\nstable last",
                Size = new Vector2(130, 100)
            };
            edit.SetLineWrappingMode(TextEditLineWrappingMode.Boundary);

            edit.GetLineWrapCount(0);
            var wrapped = edit.GetLineWrappedText(1);
            edit.GetLineWrapCount(2);
            var initialBuilds = edit.WrapLayoutBuildCount;

            Assert.Multiple(() =>
            {
                Assert.That(string.Concat(wrapped), Is.EqualTo(edit.GetLine(1)));
                Assert.That(wrapped.Any(part => part.Length > 0 && char.IsHighSurrogate(part[^1])), Is.False);
                Assert.That(wrapped.Any(part => part.Length > 0 && char.IsLowSurrogate(part[0])), Is.False);
                Assert.That(wrapped.Skip(1).Any(part => part.Length > 0 && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(part, 0) == System.Globalization.UnicodeCategory.NonSpacingMark), Is.False);
            });

            edit.SetLine(1, "A😀e\u0301 changed middle line");
            edit.GetLineWrapCount(0);
            edit.GetLineWrapCount(2);
            Assert.That(edit.WrapLayoutBuildCount, Is.EqualTo(initialBuilds));

            edit.GetLineWrapCount(1);
            Assert.That(edit.WrapLayoutBuildCount, Is.EqualTo(initialBuilds + 1));
        }

        [Test]
        public void CodeEdit_MapsGodotWrappedGuttersAndMinimapViewport()
        {
            var code = new CodeEdit
            {
                Text = "alpha beta gamma\nsecond wrapped source line",
                WrapAtColumn = 8,
                Size = new Vector2(160, 32),
                DrawMinimap = true,
                DrawLineNumbers = true,
                DrawFoldGutter = true,
            };
            code.SetLineWrappingMode(TextEditLineWrappingMode.Boundary);
            code.SetLineAsFirstVisible(0, 1);

            Assert.That(code.GetTotalVisibleLineCount(), Is.EqualTo(7));
            Assert.That(code.FirstVisibleLineWrapIndex, Is.EqualTo(1));
            Assert.That(code.GetMinimapBounds(), Is.Not.EqualTo(Rectangle.Empty));
            var viewport = code.GetMinimapViewportBounds();
            Assert.That(viewport.Top, Is.GreaterThan(code.GetMinimapBounds().Top));
            Assert.That(viewport.Bottom, Is.LessThanOrEqualTo(code.GetMinimapBounds().Bottom));
            Assert.That(viewport.Width, Is.EqualTo(code.GetMinimapBounds().Width - 2));
            code.DrawMinimap = false;
            Assert.That(code.GetMinimapBounds(), Is.EqualTo(Rectangle.Empty));
            Assert.That(code.GetMinimapViewportBounds(), Is.EqualTo(Rectangle.Empty));
        }

        [Test]
        public void DynamicTextEditAndCodeEditShareShapedLayoutsAcrossEditorFeatures()
        {
            using var latin = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabic = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var font = new DynamicUIFont(latin, 18, UIFontHinting.Default, arabic);
            var edit = new TextEdit
            {
                UIFont = font,
                Text = "var\tname = \"مرحبا\";\nnext line",
                Size = new Vector2(170, 80)
            };
            edit.SetLineWrappingMode(TextEditLineWrappingMode.Boundary);
            edit.AddGutter();
            edit.SetLineGutterText(0, 0, "1");
            var highlighter = new CodeHighlighter();
            highlighter.AddKeywordColor("var", Color.CornflowerBlue);
            highlighter.AddColorRegion("\"", "\"", Color.Orange);
            edit.SetSyntaxHighlighter(highlighter);

            var firstHighlight = edit.GetLineSyntaxHighlighting(0);
            edit.GetLineSyntaxHighlighting(1);
            edit.Select(0, 0, 0, edit.GetLine(0).Length);
            var selection = edit.GetSelectionRectangles();
            var shapedLine = edit.GetEditingLayout(edit.GetLine(0));

            Assert.Multiple(() =>
            {
                Assert.That(shapedLine.Runs.Select(run => run.Font.Identity).Distinct().Count(), Is.GreaterThanOrEqualTo(2));
                Assert.That(edit.GetLineGutterText(0, 0), Is.EqualTo("1"));
                Assert.That(firstHighlight, Is.Not.Empty);
                Assert.That(selection, Is.Not.Empty);
                Assert.That(edit.GetLineWidth(0), Is.GreaterThan(0));
            });

            edit.SetLine(1, "changed line");
            Assert.That(edit.GetLineSyntaxHighlighting(0), Is.SameAs(firstHighlight));

            var code = new CodeEdit { UIFont = font, Text = "obj.pri", Size = new Vector2(220, 80) };
            code.SetCaret(0, code.Text.Length);
            code.AddCodeCompletionOption(CodeCompletionKind.Function, "print(value)", "print");
            code.UpdateCodeCompletionOptions(forced: true);

            Assert.Multiple(() =>
            {
                Assert.That(code.IsCodeCompletionActive, Is.True);
                Assert.That(code.CodeCompletionOptions, Has.Count.EqualTo(1));
                Assert.That(code.GetEditingLayout(code.Text).Size.X, Is.GreaterThan(0));
            });
        }

        [Test]
        public void CodeEdit_ClickingMinimapScrollsToWrapAwareDocumentRow()
        {
            var lines = new List<string>();
            for (var index = 0; index < 20; index++) lines.Add($"line {index}");
            var code = new CodeEdit
            {
                Text = string.Join("\n", lines),
                Size = new Vector2(160, 48),
                DrawMinimap = true,
            };
            var minimap = code.GetMinimapBounds();

            code.PointerPressed(new Point(minimap.Center.X, minimap.Bottom - 1));

            Assert.That(code.FirstVisibleLine, Is.GreaterThanOrEqualTo(17));
            Assert.That(code.GetMinimapViewportBounds().Bottom, Is.EqualTo(minimap.Bottom));
        }

        [Test]
        public void TextEdit_MapsGodotSecondaryCaretLifecycleSelectionsAndTextInput()
        {
            var edit = new TextEdit { Text = "one\ntwo\nthree" };
            edit.SetCaret(0, 3);
            var secondary = edit.AddCaret(1, 3);

            Assert.That(secondary, Is.EqualTo(1));
            Assert.That(edit.GetCaretCount(), Is.EqualTo(2));
            Assert.That(edit.GetCaretLine(1), Is.EqualTo(1));
            Assert.That(edit.GetCaretColumn(1), Is.EqualTo(3));
            Assert.That(edit.AddCaret(1, 3), Is.EqualTo(-1), "Carets cannot overlap.");
            edit.InsertText("!");
            Assert.That(edit.Text, Is.EqualTo("one!\ntwo!\nthree"));
            Assert.That(edit.GetCaretColumn(0), Is.EqualTo(4));
            Assert.That(edit.GetCaretColumn(1), Is.EqualTo(4));

            edit.Select(0, 0, 0, 4, 0);
            edit.Select(1, 0, 1, 4, 1);
            Assert.That(edit.HasCaretSelection(0), Is.True);
            Assert.That(edit.HasCaretSelection(1), Is.True);
            Assert.That(edit.GetSelectionOriginLine(1), Is.EqualTo(1));
            Assert.That(edit.GetSelectionFrom(1), Is.EqualTo(5));
            Assert.That(edit.GetSelectionTo(1), Is.EqualTo(9));
            edit.InsertText("X");
            Assert.That(edit.Text, Is.EqualTo("X\nX\nthree"));
            Assert.That(edit.GetCarets(), Has.Count.EqualTo(2));

            edit.RemoveCaret(1);
            Assert.That(edit.GetCaretCount(), Is.EqualTo(1));
            edit.SetMultipleCaretsEnabled(false);
            Assert.That(edit.AddCaret(2, 0), Is.EqualTo(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => edit.RemoveCaret(0));

            var keyboard = new TextEdit { Text = "alpha\nbeta" };
            keyboard.SetCaret(0, 2); keyboard.AddCaret(1, 2);
            keyboard.KeyPressed(Keys.Back);
            Assert.That(keyboard.Text, Is.EqualTo("apha\nbta"));
            Assert.That(keyboard.GetCaretColumn(0), Is.EqualTo(1));
            Assert.That(keyboard.GetCaretColumn(1), Is.EqualTo(1));
            keyboard.KeyPressed(Keys.End);
            Assert.That(keyboard.GetCaretColumn(0), Is.EqualTo(4));
            Assert.That(keyboard.GetCaretColumn(1), Is.EqualTo(3));
            keyboard.KeyPressed(Keys.Home);
            Assert.That(keyboard.GetCaretColumn(0), Is.Zero);
            Assert.That(keyboard.GetCaretColumn(1), Is.Zero);
        }

        [Test]
        public void TextEdit_MapsGodotWrapAwareMultiCaretSelectionRectangles()
        {
            var edit = new TextEdit { Text = "alpha beta\ngamma", Size = new Vector2(160, 64), WrapAtColumn = 6 };
            edit.SetLineWrappingMode(TextEditLineWrappingMode.Boundary);
            edit.Select(0, 1, 0, 9, 0);
            var secondary = edit.AddCaret(1, 0);
            edit.Select(1, 0, 1, 3, secondary);

            var primary = edit.GetSelectionRectangles(0);
            Assert.That(primary, Has.Count.EqualTo(2));
            Assert.That(primary[0], Is.EqualTo(new Rectangle(14, 4, 40, 16)));
            Assert.That(primary[1], Is.EqualTo(new Rectangle(6, 20, 24, 16)));
            var secondaryRectangles = edit.GetSelectionRectangles(secondary);
            Assert.That(secondaryRectangles, Is.EqualTo(new[] { new Rectangle(6, 36, 24, 16) }));

            edit.SetLineAsFirstVisible(0, 1);
            Assert.That(edit.GetSelectionRectangles(0)[0].Y, Is.EqualTo(-12), "Selection geometry follows the wrap-row viewport offset.");
            edit.RemoveSecondaryCarets();
            Assert.That(edit.GetCaretCount(), Is.EqualTo(1));
        }

        [Test]
        public void TextEdit_MapsGodotOverlappingCaretMergePolicy()
        {
            var edit = new TextEdit { Text = "abcdef" };
            edit.SetCaret(0, 1);
            edit.AddCaret(0, 4);
            edit.Select(0, 1, 0, 4, 0);
            Assert.That(edit.GetCaretCount(), Is.EqualTo(1), "A caret on a selection edge merges into that selection.");
            Assert.That(edit.SelectionFrom, Is.EqualTo(1));
            Assert.That(edit.SelectionTo, Is.EqualTo(4));

            edit = new TextEdit { Text = "abcdef" };
            edit.SetCaret(0, 1); edit.AddCaret(0, 4);
            edit.KeyPressed(Keys.Home);
            Assert.That(edit.GetCaretCount(), Is.EqualTo(1), "Converging navigation merges redundant caret points.");
            Assert.That(edit.GetCaretColumn(), Is.Zero);
        }

        [Test]
        public void VirtualJoystick_CapturesPointerAndResetsOnRelease()
        {
            var joystick = new VirtualJoystick { Size = new Vector2(100, 100) };
            var context = new UIContext(); context.Add(joystick);
            context.Update(Time, Mouse(50, 50), new KeyboardState());
            context.Update(Time, Mouse(50, 50, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(95, 50, ButtonState.Pressed), new KeyboardState());

            Assert.That(joystick.Value.X, Is.GreaterThan(.8f));
            Assert.That(joystick.IsPressed, Is.True);
            context.Update(Time, Mouse(95, 50), new KeyboardState());
            Assert.That(joystick.Value, Is.EqualTo(Vector2.Zero));
            Assert.That(joystick.IsPressed, Is.False);
        }

        [Test]
        public void VirtualJoystick_AllowsExplicitVisualColors()
        {
            var joystick = new VirtualJoystick { BackgroundColor = Color.DarkSlateGray, KnobColor = Color.CornflowerBlue };

            Assert.That(joystick.BackgroundColor, Is.EqualTo(Color.DarkSlateGray));
            Assert.That(joystick.KnobColor, Is.EqualTo(Color.CornflowerBlue));
        }

        [Test]
        public void ThemeStyleBoxes_ResolveLocalThenTypedThenSharedOverrides()
        {
            var theme = new Theme();
            var shared = new StyleBoxEmpty();
            var typed = new StyleBoxFlat { BackgroundColor = Color.Red };
            var local = new StyleBoxFlat { BackgroundColor = Color.Blue };
            theme.SetStyleBox("panel", shared);
            theme.SetStyleBox("panel", typed, nameof(Panel));
            var context = new UIContext { Theme = theme };
            var panel = new Panel(); context.Add(panel);

            Assert.That(panel.GetThemeStyleBox("panel"), Is.SameAs(typed));
            panel.AddThemeStyleOverride("panel", local);
            Assert.That(panel.GetThemeStyleBox("panel"), Is.SameAs(local));
            panel.RemoveThemeStyleOverride("panel");
            Assert.That(panel.GetThemeStyleBox("panel"), Is.SameAs(typed));
        }

        [Test]
        public void Themes_InheritStyleItemsColorsAndBaseControlTypeItems()
        {
            var root = new Theme
            {
                AccentColor = Color.Orange,
                TabSelectedColor = Color.DarkBlue,
                TabSelectedIndicatorColor = Color.Gold,
                TabSelectedIndicatorHeight = 4,
                TabSelectedLift = 6,
            };
            var baseButtonStyle = new StyleBoxFlat { BackgroundColor = Color.CornflowerBlue };
            root.SetStyleBox("normal", baseButtonStyle, nameof(BaseButton));
            var contextual = new Theme { Parent = root };
            var context = new UIContext { Theme = root };
            var panel = new Panel { ThemeOverride = contextual };
            var button = new Button();
            panel.AddChild(button); context.Add(panel);

            Assert.That(contextual.AccentColor, Is.EqualTo(Color.Orange));
            Assert.That(contextual.TabSelectedColor, Is.EqualTo(Color.DarkBlue));
            Assert.That(contextual.TabSelectedIndicatorColor, Is.EqualTo(Color.Gold));
            Assert.That(contextual.TabSelectedIndicatorHeight, Is.EqualTo(4));
            Assert.That(contextual.TabSelectedLift, Is.EqualTo(6));
            Assert.That(button.GetThemeStyleBox("normal"), Is.SameAs(baseButtonStyle));
        }

        [Test]
        public void Themes_RejectCyclicInheritance()
        {
            var root = new Theme();
            var child = new Theme { Parent = root };

            Assert.That(() => root.Parent = root, Throws.ArgumentException);
            Assert.That(() => root.Parent = child, Throws.ArgumentException);
        }

        [Test]
        public void ThemeIcons_ResolveLocalTypedSharedAndParentEntries()
        {
            var texture = (Texture2D)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var shared = new ThemeIcon(texture, new Rectangle(0, 0, 8, 8), new Point(8, 8));
            var baseTyped = new ThemeIcon(texture, new Rectangle(8, 0, 10, 10), new Point(10, 10));
            var derivedTyped = new ThemeIcon(texture, new Rectangle(18, 0, 12, 12), new Point(12, 12));
            var local = new ThemeIcon(texture, new Rectangle(30, 0, 14, 14), new Point(14, 14));
            var parent = new Theme();
            parent.SetIcon("state", shared);
            parent.SetIcon("state", baseTyped, nameof(BaseButton));
            var child = new Theme { Parent = parent };
            child.SetIcon("state", derivedTyped, nameof(Button));
            var context = new UIContext { Theme = child };
            var button = new Button();
            context.Add(button);

            Assert.That(button.GetThemeIcon("state"), Is.EqualTo(derivedTyped));
            child.RemoveIcon("state", nameof(Button));
            Assert.That(button.GetThemeIcon("state"), Is.EqualTo(baseTyped));
            button.AddThemeIconOverride("state", local);
            Assert.That(button.GetThemeIcon("state"), Is.EqualTo(local));
            button.RemoveThemeIconOverride("state");
            Assert.That(button.GetThemeIcon("state"), Is.EqualTo(baseTyped));
            Assert.That(new Control { ThemeOverride = child }.GetThemeIcon("missing"), Is.Null);
        }

        [Test]
        public void ThemeIcons_SuppressionStopsInheritanceUntilRemoved()
        {
            var texture = (Texture2D)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var inherited = new ThemeIcon(texture, new Rectangle(0, 0, 8, 8), new Point(8, 8));
            var root = new Theme();
            root.SetIcon("arrow", inherited, nameof(OptionButton));
            var child = new Theme { Parent = root };
            child.SuppressIcon("arrow", nameof(OptionButton));
            var context = new UIContext { Theme = child };
            var option = new OptionButton();
            context.Add(option);

            Assert.That(option.GetThemeIcon("arrow"), Is.Null);
            child.RemoveIcon("arrow", nameof(OptionButton));
            Assert.That(option.GetThemeIcon("arrow"), Is.EqualTo(inherited));
            option.SuppressThemeIcon("arrow");
            Assert.That(option.GetThemeIcon("arrow"), Is.Null);
            option.RemoveThemeIconOverride("arrow");
            Assert.That(option.GetThemeIcon("arrow"), Is.EqualTo(inherited));
        }

        [Test]
        public void ThemeIcon_IsImmutableAndDoesNotOwnItsTexture()
        {
            var texture = (Texture2D)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var icon = new ThemeIcon(texture, new Rectangle(4, 6, 20, 24), new Point(10, 12), 2);

            Assert.That(icon.Texture, Is.SameAs(texture));
            Assert.That(icon.SourceRectangle, Is.EqualTo(new Rectangle(4, 6, 20, 24)));
            Assert.That(icon.LogicalSize, Is.EqualTo(new Point(10, 12)));
            Assert.That(icon.Density, Is.EqualTo(2));
            Assert.That(icon, Is.Not.InstanceOf<IDisposable>());
        }

        [Test]
        public void ThemeIcon_RuntimeSvgFallbackPreservesLogicalAndAtlasMetadata()
        {
            var texture = (Texture2D)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var source = SvgImageSource.FromMemory(System.Text.Encoding.UTF8.GetBytes(
                "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='6'><rect width='10' height='6'/></svg>"));
            var icon = new ThemeIcon(source, texture, new Rectangle(8, 12, 20, 12), new Point(10, 6), 2);

            Assert.Multiple(() =>
            {
                Assert.That(icon.ScalableSource, Is.SameAs(source));
                Assert.That(icon.Texture, Is.SameAs(texture));
                Assert.That(icon.SourceRectangle, Is.EqualTo(new Rectangle(8, 12, 20, 12)));
                Assert.That(icon.LogicalSize, Is.EqualTo(new Point(10, 6)));
                Assert.That(icon.Density, Is.EqualTo(2));
            });
        }

        [Test]
        public void DefaultThemeSvgSourceFailureFallsBackPerIcon()
        {
            var fallbackCount = 0;
            var source = DefaultThemeIconResources.TryGetSvgSource(
                () => throw new SvgLoadException(SvgLoadErrorCode.UnsupportedFeature, "unsupported fixture"),
                () => fallbackCount++);

            Assert.Multiple(() =>
            {
                Assert.That(source, Is.Null);
                Assert.That(fallbackCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void DefaultThemeSvgSourceFailureFallsBackForAllCaughtExceptionTypes()
        {
            var exceptionFactories = new Func<Exception>[]
            {
                () => new SvgLoadException(SvgLoadErrorCode.UnsupportedFeature, "unsupported"),
                () => new InvalidOperationException("no backend"),
                () => new IOException("file read error"),
                () => new UnauthorizedAccessException("access denied"),
                () => new NotSupportedException("not supported source type"),
            };
            foreach (var factory in exceptionFactories)
            {
                var fallbackCount = 0;
                var capturedFactory = factory;
                var result = DefaultThemeIconResources.TryGetSvgSource(
                    () => throw capturedFactory(),
                    () => fallbackCount++);
                Assert.That(result, Is.Null, $"TryGetSvgSource must return null for {capturedFactory().GetType().Name}.");
                Assert.That(fallbackCount, Is.EqualTo(1), $"TryGetSvgSource must record exactly one fallback for {capturedFactory().GetType().Name}.");
            }
        }

        [TestCase(0.75f, 1)]
        [TestCase(1f, 1)]
        [TestCase(1.25f, 1)]
        [TestCase(1.49f, 1)]
        [TestCase(1.5f, 2)]
        [TestCase(2f, 2)]
        [TestCase(3f, 2)]
        [TestCase(float.NaN, 1)]
        public void ThemeIcons_SelectDensityAtDocumentedThreshold(float displayScale, int expectedDensity)
        {
            Assert.That(DefaultThemeIconResources.SelectDensity(displayScale), Is.EqualTo(expectedDensity));
        }

        [Test]
        public void AdvancedThemeIcons_PreserveGraphPortGeometryAndExplicitTexturePrecedence()
        {
            var texture = (Texture2D)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var theme = new Theme();
            theme.SetIcon("port", new ThemeIcon(texture, new Rectangle(0, 0, 10, 6), new Point(10, 6)), nameof(GraphNode));
            var context = new UIContext { Theme = theme };
            var node = new GraphNode { Size = new Vector2(120, 80) };
            node.SetSlot(0, true, 0, Color.White, true, 0, Color.White);
            context.Add(node);

            Assert.That(node.GetInputPortDrawBounds(0), Is.EqualTo(new Rectangle(0, 31, 10, 6)));
            Assert.That(node.GetOutputPortDrawBounds(0), Is.EqualTo(new Rectangle(110, 31, 10, 6)));

            node.SetSlot(0, true, 0, Color.White, true, 0, Color.White, texture, texture);
            Assert.That(node.GetInputPortDrawBounds(0), Is.EqualTo(new Rectangle(0, 28, 12, 12)));
            Assert.That(node.GetOutputPortDrawBounds(0), Is.EqualTo(new Rectangle(108, 28, 12, 12)));
        }

        [Test]
        public void AdvancedThemeIcons_SelectFoldableDirectionAndGraphToolbarOwnerBindings()
        {
            var texture = (Texture2D)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var icon = new ThemeIcon(texture, new Rectangle(0, 0, 8, 8), new Point(8, 8));
            var theme = new Theme();
            theme.SetIcon("zoom_out", icon, nameof(GraphEdit));
            var context = new UIContext { Theme = theme };
            var graph = new GraphEdit();
            context.Add(graph);

            Assert.That(graph.ZoomOutButton.DecorativeIconProvider(), Is.EqualTo(icon));
            Assert.That(graph.ZoomInButton.DecorativeIconProvider(), Is.Null, "A missing optional sibling icon must remain a graceful text fallback.");

            var foldable = new FoldableContainer();
            Assert.That(foldable.GetArrowIconName(), Is.EqualTo("expanded_arrow"));
            foldable.Folded = true;
            Assert.That(foldable.GetArrowIconName(), Is.EqualTo("folded_arrow"));
            foldable.LayoutDirection = LayoutDirection.RightToLeft;
            Assert.That(foldable.GetArrowIconName(), Is.EqualTo("folded_arrow_mirrored"));
            foldable.Folded = false;
            Assert.That(foldable.GetArrowIconName(), Is.EqualTo("expanded_arrow_mirrored"));
        }

        [Test]
        public void Tooltip_AppearsAfterDelayAndUsesNearestAncestorWithText()
        {
            var context = new UIContext { TooltipDelay = TimeSpan.FromMilliseconds(100) };
            var parent = new Control { Size = new Vector2(100, 40), TooltipText = "Parent help" };
            // MouseFilter.Pass lets tooltip resolution bubble to the parent, matching Godot's
            // Viewport::_gui_get_tooltip, which only continues searching ancestors past a control
            // whose own mouse filter is not Stop.
            var child = new Control { Position = new Vector2(10, 10), Size = new Vector2(30, 20), MouseFilter = MouseFilter.Pass };
            parent.AddChild(child); context.Add(parent);

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(20, 20), new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120)), Mouse(20, 20), new KeyboardState());

            Assert.That(context.IsTooltipVisible, Is.True);
            Assert.That(context.TooltipOwner, Is.SameAs(parent));
            Assert.That(context.TooltipText, Is.EqualTo("Parent help"));
            context.Update(new GameTime(TimeSpan.FromMilliseconds(130), TimeSpan.FromMilliseconds(10)), Mouse(150, 60), new KeyboardState());
            Assert.That(context.IsTooltipVisible, Is.False);
        }

        [Test]
        public void Tooltip_DoesNotBubbleToAncestorPastAControlThatStopsMouseFilterLikeGodot()
        {
            var context = new UIContext { TooltipDelay = TimeSpan.FromMilliseconds(100) };
            var parent = new Control { Size = new Vector2(100, 40), TooltipText = "Parent help" };
            // Default MouseFilter is Stop, matching Godot's Viewport::_gui_get_tooltip breaking the
            // ancestor search the instant it reaches a control whose own filter is Stop, even though
            // that control's own tooltip is empty.
            var child = new Control { Position = new Vector2(10, 10), Size = new Vector2(30, 20) };
            parent.AddChild(child); context.Add(parent);

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(20, 20), new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120)), Mouse(20, 20), new KeyboardState());

            Assert.That(context.IsTooltipVisible, Is.False);
        }

        [Test]
        public void PanelContainer_UsesStyleBoxContentMarginsForLayoutAndMinimumSize()
        {
            var style = new StyleBoxFlat { ContentMargin = new Thickness(6, 7, 8, 9) };
            var theme = new Theme(); theme.SetStyleBox("panel", style, nameof(PanelContainer));
            var panel = new PanelContainer { Size = new Vector2(100, 80) };
            var child = new Control { CustomMinimumSize = new Vector2(20, 10) };
            panel.AddChild(child);
            var context = new UIContext { Theme = theme }; context.Add(panel); context.Layout();

            Assert.That(panel.GetMinimumSize(), Is.EqualTo(new Vector2(34, 26)));
            Assert.That(child.Position, Is.EqualTo(new Vector2(6, 7)));
            Assert.That(child.Size, Is.EqualTo(new Vector2(86, 64)));
        }

        [Test]
        public void FileDialog_SortsFilesByExtensionWhenTypeSortIsSelected()
        {
            var path = Path.Combine(Path.GetTempPath(), "monogame-ui-sort-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                File.WriteAllText(Path.Combine(path, "zeta.cs"), string.Empty);
                File.WriteAllText(Path.Combine(path, "alpha.txt"), string.Empty);
                File.WriteAllText(Path.Combine(path, "beta.cs"), string.Empty);
                var dialog = new FileDialog { SortOption = FileDialogSortOption.Type };
                dialog.Refresh(path);

                Assert.That(Path.GetFileName(dialog.Entries[0]), Is.EqualTo("beta.cs"));
                Assert.That(Path.GetFileName(dialog.Entries[1]), Is.EqualTo("zeta.cs"));
                Assert.That(Path.GetFileName(dialog.Entries[2]), Is.EqualTo("alpha.txt"));
            }
            finally
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
        }

        [Test]
        public void RichTextDocument_StripsTagsAndAppliesEffects()
        {
            var document = new RichTextDocument();
            document.InstallEffect(new UpperCaseEffect());
            document.AppendBbcode("[b]hello[/b]");

            Assert.That(document.Text, Is.EqualTo("HELLO"));
        }

        [Test]
        public void RichTextLabel_ParsesNestedBbcodeIntoStyledSpans()
        {
            var label = new RichTextLabel();
            label.AppendBbcode("[color=#ff0000]red [b]bold[/b][/color][br][u]under[/u]");

            Assert.That(label.Text, Is.EqualTo("red bold\nunder"));
            Assert.That(label.Spans.Count, Is.EqualTo(4));
            Assert.That(label.Spans[0].Color, Is.EqualTo(Color.Red));
            Assert.That(label.Spans[1].Bold, Is.True);
            Assert.That(label.Spans[1].Color, Is.EqualTo(Color.Red));
            Assert.That(label.Spans[3].Underline, Is.True);
        }

        [Test]
        public void RichTextLabelDynamicLayoutWrapsAndMapsWholeGraphemeClusters()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 18);
            var emojiWidth = TextMetrics.Measure(font, "😀").X;
            var richText = new RichTextLabel
            {
                UIFont = font,
                Text = "😀😀",
                AutowrapMode = LabelAutowrapMode.Arbitrary,
                Size = new Vector2(emojiWidth + 1 + 6, 80),
            };

            Assert.Multiple(() =>
            {
                Assert.That(richText.GetLineCount(), Is.EqualTo(2));
                Assert.That(richText.GetLineRange(0), Is.EqualTo(new Point(0, 2)));
                Assert.That(richText.GetLineRange(1), Is.EqualTo(new Point(2, 4)));
            });
        }

        [Test]
        public void RichTextLabelWordWrapUsesRemainingWidthAcrossStyledSpanBoundaries()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            const string remainder = ". Use the property inspector to change values.";
            var fonts = new UIFont[] { new DynamicUIFont(face, 16), new SpriteFontAdapter(CreateTestFont(), 16) };
            foreach (var font in fonts)
            {
                var availableWidth = TextMetrics.Measure(font, ". Use the property ").X + .5f;
                var richText = new RichTextLabel { UIFont = font, AutowrapMode = LabelAutowrapMode.Word };
                richText.AppendBbcode("Interactive example of [color=#30b9a4]Forma.OptionButton[/color]" + remainder);

                var prefixLength = richText.GetFittingWrapPrefixLength(remainder, availableWidth);

                Assert.Multiple(() =>
                {
                    Assert.That(prefixLength, Is.GreaterThan(2), $"{font.GetType().Name} should keep the period and following words on the current line.");
                    Assert.That(prefixLength, Is.LessThan(remainder.Length));
                    Assert.That(char.IsWhiteSpace(remainder[prefixLength - 1]), Is.True, "Word wrapping should stop at a Unicode line-break opportunity.");
                });
            }
        }

        [Test]
        public void RichTextLabelLetterSpacingAdjustsMeasuredGraphemeAdvances()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 16, UIFontHinting.Light);
            var natural = new RichTextLabel { UIFont = font, Text = "runtime.", Padding = Thickness.Zero };
            var tracked = new RichTextLabel { UIFont = font, Text = "runtime.", Padding = Thickness.Zero, LetterSpacing = .25f };

            Assert.Multiple(() =>
            {
                Assert.That(tracked.GetMinimumSize().X - natural.GetMinimumSize().X, Is.EqualTo(1.75f).Within(.0001f));
                Assert.That(() => tracked.LetterSpacing = float.NaN, Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void RichTextLabel_DirectTextAssignmentResetsPriorStyledDocumentBeforeAppending()
        {
            var label = new RichTextLabel();
            label.AppendBbcode("[b]Old[/b]");
            label.Text = "New";
            label.AppendText(" text");

            Assert.That(label.Text, Is.EqualTo("New text"));
            Assert.That(label.Spans.Count, Is.EqualTo(1));
            Assert.That(label.Spans[0].Text, Is.EqualTo("New text"));
            Assert.That(label.Spans[0].Bold, Is.False);
        }

        [Test]
        public void RichTextLabel_ParsesGodotMetadataLinksBackgroundsAndProgrammaticStyleStack()
        {
            var label = new RichTextLabel();
            label.AppendBbcode("[bgcolor=#112233][url=res://docs/ui.md][b]Open docs[/b][/url][/bgcolor]");

            Assert.That(label.Text, Is.EqualTo("Open docs"));
            Assert.That(label.Spans, Has.Count.EqualTo(1));
            Assert.That(label.Spans[0].BackgroundColor, Is.EqualTo(new Color(0x11, 0x22, 0x33)));
            Assert.That(label.Spans[0].Meta, Is.EqualTo("res://docs/ui.md"));
            Assert.That(label.Spans[0].Bold, Is.True);

            label.Clear(); label.AppendBbcode("[url]https://docs.godotengine.org[/url]");
            Assert.That(label.Spans[0].Meta, Is.EqualTo("https://docs.godotengine.org"));

            label.Clear();
            label.PushBgColor(Color.DarkSlateGray); label.PushMeta("inspector://layer"); label.PushUnderline(); label.AppendText("Layer"); label.Pop(); label.Pop(); label.Pop(); label.AppendText(" value");
            Assert.That(label.Text, Is.EqualTo("Layer value"));
            Assert.That(label.Spans[0].Meta, Is.EqualTo("inspector://layer"));
            Assert.That(label.Spans[0].BackgroundColor, Is.EqualTo(Color.DarkSlateGray));
            Assert.That(label.Spans[0].Underline, Is.True);
            Assert.That(label.Spans[1].Meta, Is.Null);

            label.Clear(); label.PushBgColor(Color.DarkSlateGray); label.PushMeta("inspector://merged"); label.AppendText("A"); label.AppendText("B");
            Assert.That(label.Spans, Has.Count.EqualTo(1));
            Assert.That(label.Spans[0].Text, Is.EqualTo("AB"));
            Assert.That(label.Spans[0].BackgroundColor, Is.EqualTo(Color.DarkSlateGray));
            Assert.That(label.Spans[0].Meta, Is.EqualTo("inspector://merged"));
        }

        [Test]
        public void RichTextLabel_MapsGodotIndentAndBasicListDocumentStructure()
        {
            var label = new RichTextLabel();
            label.AppendBbcode("[indent]Indented[/indent][br][ul][*]First[ul][*]Nested[/ul][*]Second[/ul][br][ol type=A][*]One[*]Two[/ol]");

            Assert.That(label.Text, Is.EqualTo("    Indented\n• First\n    • Nested\n• Second\nA. One\nB. Two"));

            label.Clear(); label.PushList(2, RichTextListType.Letters, capitalize: true); label.AppendText("Alpha"); label.Pop(); label.PushIndent(2); label.AppendText("Nested");
            Assert.That(label.Text, Is.EqualTo("    A. Alpha        Nested"));
        }

        [Test]
        public void RichTextLabel_MapsGodotTableCellsAndTabStopProjection()
        {
            var label = new RichTextLabel { TableCellWidth = 72 };
            label.AppendBbcode("[table=2][cell]Property[/cell][cell]Value[/cell][cell]Mode[/cell][cell]Linear[/cell][/table]");
            Assert.That(label.Text, Is.EqualTo("Property\tValue\nMode\tLinear"));

            label.Clear(); label.PushTable(2, "Inspector");
            label.PushCell(); label.AppendText("Key"); label.PushCell(); label.AppendText("Value"); label.Pop();
            Assert.That(label.Text, Is.EqualTo("Key\tValue"));
            Assert.That(label.GetCurrentTableColumn(), Is.EqualTo(-1));
        }

        [Test]
        public void RichTextLabel_MeasuresTableTabCharactersWithoutCrashingLikeGodotColumnLayout()
        {
            // Regression test: a table's plain-text projection embeds '\t' between cells (see the test
            // above), but SpriteFont.MeasureString only special-cases '\r'/'\n', not '\t'. A font with no
            // DefaultCharacter (as this project's bundled test/sample font is configured) used to throw
            // ArgumentException out of GetMinimumSize for any RichTextLabel containing a table, which
            // crashed Samples/UIComparison/MonoGameEditorMock on startup.
            var font = CreateTestFont();
            var label = new RichTextLabel { Font = font, TableCellWidth = 72 };
            label.AppendBbcode("[table=2][cell]Property[/cell][cell]Value[/cell][/table]");

            Vector2 minimumSize = default;
            Assert.DoesNotThrow(() => minimumSize = label.GetMinimumSize(), "Font.MeasureString cannot resolve a glyph for '\\t'; GetMinimumSize must not measure the raw tab character.");
            Assert.That(minimumSize.X, Is.GreaterThan(0));

            label.FitContent = true;
            Assert.DoesNotThrow(() => label.GetMinimumSize(), "The FitContent branch measures Text directly too and must be equally tab-safe.");
        }

        [Test]
        public void RichTextLabel_MapsGodotHorizontalRuleDocumentBlocks()
        {
            var label = new RichTextLabel();
            label.AppendBbcode("Top[hr width=50% height=3% color=#478ce7 align=right]Bottom");

            Assert.That(label.Text, Is.EqualTo("TopBottom"));
            Assert.That(label.Spans, Has.Count.EqualTo(3));
            var rule = label.Spans[1];
            Assert.That(rule.IsHorizontalRule, Is.True);
            Assert.That(rule.RuleWidth, Is.EqualTo(50));
            Assert.That(rule.RuleWidthInPercent, Is.True);
            Assert.That(rule.RuleHeight, Is.EqualTo(3));
            Assert.That(rule.RuleHeightInPercent, Is.True);
            Assert.That(rule.RuleColor, Is.EqualTo(new Color(71, 140, 231)));
            Assert.That(rule.RuleAlignment, Is.EqualTo(HorizontalAlignment.Right));

            label.Clear(); label.AddHorizontalRule(40, 2, Color.Orange, HorizontalAlignment.Left, widthInPercent: false, heightInPercent: true);
            Assert.That(label.Text, Is.Empty);
            Assert.That(label.Spans[0].IsHorizontalRule, Is.True);
            Assert.That(label.Spans[0].RuleWidthInPercent, Is.False);
            Assert.That(label.Spans[0].RuleHeightInPercent, Is.True);
            Assert.That(label.Spans[0].RuleColor, Is.EqualTo(Color.Orange));

            label.Clear(); label.AddHorizontalRule();
            Assert.That(label.Spans[0].RuleColor, Is.EqualTo(Color.White));
        }

        [Test]
        public void RichTextLabel_MapsGodotDocumentScrollMetricsAndWheelViewport()
        {
            var label = new RichTextLabel { Size = new Vector2(100, 40), Padding = Thickness.Zero, ScrollActive = true };
            label.AppendText("One\nTwo\nThree\nFour");
            Assert.That(label.GetLineCount(), Is.EqualTo(4));
            Assert.That(label.GetParagraphCount(), Is.EqualTo(4));
            Assert.That(label.GetLineRange(1), Is.EqualTo(new Point(4, 7)));
            Assert.That(label.GetLineOffset(2), Is.EqualTo(32));
            Assert.That(label.GetParagraphOffset(3), Is.EqualTo(48));
            Assert.That(label.GetLineHeight(0), Is.EqualTo(16));
            Assert.That(label.GetLineWidth(0), Is.EqualTo(24));
            Assert.That(label.GetContentWidth(), Is.EqualTo(40));
            Assert.That(label.GetContentHeight(), Is.EqualTo(64));
            Assert.That(label.GetScrollMaximum(), Is.EqualTo(24));
            Assert.That(label.GetVisibleContentRect(), Is.EqualTo(new Rectangle(0, 0, 100, 40)));

            var context = new UIContext(); context.Add(label);
            context.Update(Time, new MouseState(10, 10, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            var scrollBar = label.GetVScrollBar();
            Assert.That(label.IsVerticalScrollBarVisible, Is.True);
            Assert.That(scrollBar.Bounds, Is.EqualTo(new Rectangle(86, 0, 14, 40)));
            Assert.That(scrollBar.Page, Is.EqualTo(40));
            Assert.That(label.GetVisibleContentRect(), Is.EqualTo(new Rectangle(0, 0, 86, 40)));
            scrollBar.Value = 24;
            Assert.That(label.ScrollOffset, Is.EqualTo(24));

            label.KeyPressed(Keys.Home);
            Assert.That(label.ScrollOffset, Is.Zero);
            label.KeyPressed(Keys.PageDown);
            Assert.That(label.ScrollOffset, Is.EqualTo(24));
            label.KeyPressed(Keys.Up);
            Assert.That(label.ScrollOffset, Is.EqualTo(8));
            label.KeyPressed(Keys.Down);
            Assert.That(label.ScrollOffset, Is.EqualTo(24));
            label.KeyPressed(Keys.PageUp);
            Assert.That(label.ScrollOffset, Is.Zero);
            label.KeyPressed(Keys.End);
            Assert.That(label.ScrollOffset, Is.EqualTo(24));

            label.ScrollToLine(3);
            Assert.That(label.ScrollOffset, Is.EqualTo(24));
            Assert.That(label.GetVisibleLineCount(), Is.EqualTo(3));
            Assert.That(label.GetVisibleParagraphCount(), Is.EqualTo(3));
            label.ScrollToParagraph(2);
            Assert.That(label.ScrollOffset, Is.EqualTo(24), "A paragraph beyond the bottom is clamped to the retained scroll maximum.");
            context.Update(Time, new MouseState(10, 10, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            context.Update(Time, new MouseState(10, 10, 120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(label.GetScrollOffset(), Is.EqualTo(8), "A positive wheel delta scrolls toward the document start.");

            label.ScrollActive = false;
            Assert.That(label.IsVerticalScrollBarVisible, Is.False);
            label.ScrollToLine(3);
            Assert.That(label.ScrollOffset, Is.EqualTo(24), "Godot allows programmatic scrolling when scroll_active is false.");
            context.Update(Time, new MouseState(10, 10, 240, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(label.ScrollOffset, Is.EqualTo(24), "The retained wheel viewport is disabled with scroll_active.");

            label.ScrollFollowing = true;
            label.AppendText("\nFive");
            Assert.That(label.ScrollOffset, Is.EqualTo(label.GetScrollMaximum()));
        }

        [Test]
        public void RichTextLabel_MapsGodotSelectionStateAndPlainTextProjection()
        {
            var label = new RichTextLabel();
            label.AppendBbcode("[b]Node[/b] status");

            label.SelectAll();
            Assert.That(label.GetSelectionFrom(), Is.EqualTo(-1));
            Assert.That(label.GetSelectedText(), Is.Empty);

            label.SelectionEnabled = true;
            label.Select(11, 4);
            Assert.That(label.HasSelection, Is.True);
            Assert.That(label.GetSelectionFrom(), Is.EqualTo(4));
            Assert.That(label.GetSelectionTo(), Is.EqualTo(11));
            Assert.That(label.GetSelectedText(), Is.EqualTo(" status"));

            label.SelectAll();
            Assert.That(label.GetSelectedText(), Is.EqualTo("Node status"));
            label.Deselect();
            Assert.That(label.HasSelection, Is.False);
            Assert.That(label.GetSelectionFrom(), Is.EqualTo(-1));

            label.Select(2, 2);
            Assert.That(label.GetSelectionTo(), Is.EqualTo(-1));
            label.SelectAll(); label.SelectionEnabled = false;
            Assert.That(label.GetSelectedText(), Is.Empty);
        }

        [Test]
        public void RichTextLabel_MapsGodotBasicPointerDragSelection()
        {
            var label = new RichTextLabel { Size = new Vector2(160, 24), SelectionEnabled = true };
            label.AppendText("Node status");
            var context = new UIContext(); context.Add(label);

            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(4, 6, left: ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(43, 6, left: ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(43, 6), new KeyboardState());

            Assert.That(context.FocusedControl, Is.EqualTo(label));
            Assert.That(label.GetSelectionFrom(), Is.EqualTo(0));
            Assert.That(label.GetSelectionTo(), Is.EqualTo(5));
            Assert.That(label.GetSelectedText(), Is.EqualTo("Node "));
        }

        [Test]
        public void RichTextLabel_MapsGodotSelectionAutoScrollBeyondViewport()
        {
            var label = new RichTextLabel { Size = new Vector2(100, 32), Padding = Thickness.Zero, SelectionEnabled = true, ScrollActive = true };
            label.AppendText("one\ntwo\nthree\nfour");
            var context = new UIContext(); context.Add(label);
            var first = new GameTime(TimeSpan.Zero, TimeSpan.Zero);
            var held = new GameTime(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
            var heldAgain = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50));

            context.Update(first, Mouse(4, 6), new KeyboardState());
            context.Update(first, Mouse(4, 6, left: ButtonState.Pressed), new KeyboardState());
            context.Update(held, Mouse(4, 50, left: ButtonState.Pressed), new KeyboardState());
            context.Update(heldAgain, Mouse(4, 50, left: ButtonState.Pressed), new KeyboardState());
            context.Update(heldAgain, Mouse(4, 50), new KeyboardState());

            Assert.That(label.ScrollOffset, Is.GreaterThan(0), "A captured selection beyond the viewport scrolls toward the held pointer.");
            Assert.That(label.GetSelectionTo(), Is.GreaterThan(3), "The range is recalculated from the clamped viewport edge after each scroll step.");

            var disabled = new RichTextLabel { Size = new Vector2(100, 32), Padding = Thickness.Zero, SelectionEnabled = true, ScrollActive = true, SelectionAutoScrollEnabled = false };
            disabled.AppendText("one\ntwo\nthree\nfour");
            var disabledContext = new UIContext(); disabledContext.Add(disabled);
            disabledContext.Update(first, Mouse(4, 6), new KeyboardState());
            disabledContext.Update(first, Mouse(4, 6, left: ButtonState.Pressed), new KeyboardState());
            disabledContext.Update(held, Mouse(4, 50, left: ButtonState.Pressed), new KeyboardState());
            Assert.That(disabled.ScrollOffset, Is.Zero);
        }

        [Test]
        public void RichTextLabel_MapsGodotScrollToSelection()
        {
            var label = new RichTextLabel { Size = new Vector2(100, 32), Padding = Thickness.Zero, SelectionEnabled = true, ScrollActive = false };
            label.AppendText("one\ntwo\nthree\nfour");

            label.Select(8, 13);
            Assert.That(label.GetSelectionLineOffset(), Is.EqualTo(32));
            label.ScrollToSelection();
            Assert.That(label.ScrollOffset, Is.EqualTo(32), "Godot permits programmatic selection scrolling even when wheel scrolling is disabled.");

            label.SetScrollOffset(0);
            label.Deselect();
            Assert.That(label.GetSelectionLineOffset(), Is.EqualTo(-1));
            label.ScrollToSelection();
            Assert.That(label.ScrollOffset, Is.Zero);
        }

        [Test]
        public void RichTextLabel_MapsGodotSearchSelectionAndReveal()
        {
            var label = new RichTextLabel { Size = new Vector2(100, 16), Padding = Thickness.Zero, SelectionEnabled = true };
            label.AppendText("Alpha\nbeta Alpha\nbeta");

            Assert.That(label.Search("alpha"), Is.True);
            Assert.That(label.GetSelectionFrom(), Is.EqualTo(0));
            Assert.That(label.GetSelectedText(), Is.EqualTo("Alpha"));
            Assert.That(label.ScrollOffset, Is.Zero);

            Assert.That(label.Search("alpha", fromSelection: true), Is.True);
            Assert.That(label.GetSelectionFrom(), Is.EqualTo(11));
            Assert.That(label.ScrollOffset, Is.EqualTo(16));
            Assert.That(label.Search("alpha", fromSelection: true, searchPrevious: true), Is.True);
            Assert.That(label.GetSelectionFrom(), Is.Zero);
            Assert.That(label.Search("missing"), Is.False);
            Assert.That(label.Search(string.Empty), Is.False);
            Assert.That(label.HasSelection, Is.False);

            label.SelectionEnabled = false;
            Assert.That(label.Search("alpha"), Is.False);
        }

        [Test]
        public void RichTextLabel_MapsGodotFocusLossAndSelectionDragPolicies()
        {
            var label = new RichTextLabel { Size = new Vector2(160, 24), SelectionEnabled = true };
            label.AppendText("Node status"); label.Select(0, 4);
            var other = new Button { Position = new Vector2(180, 0), Size = new Vector2(40, 24) };
            var context = new UIContext(); context.Add(label); context.Add(other);
            label.GrabFocus(); other.GrabFocus();
            Assert.That(label.HasSelection, Is.False, "Focus loss deselects by default.");

            label.Select(0, 4); label.DeselectOnFocusLossEnabled = false; label.GrabFocus(); other.GrabFocus();
            Assert.That(label.GetSelectedText(), Is.EqualTo("Node"));
            label.DeselectOnFocusLossEnabled = true;
            Assert.That(label.HasSelection, Is.False, "Enabling the policy while unfocused clears an existing range.");

            label.Select(0, 4); label.GrabFocus();
            object dragged = null; label.DragStarted += (_, data) => dragged = data;
            context.Update(Time, Mouse(4, 6), new KeyboardState());
            context.Update(Time, Mouse(4, 6, left: ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(24, 6, left: ButtonState.Pressed), new KeyboardState());
            Assert.That(dragged, Is.EqualTo("Node"));

            context.Update(Time, Mouse(24, 6), new KeyboardState());
            label.Select(0, 4); label.DragAndDropSelectionEnabled = false;
            context.Update(Time, Mouse(4, 6, left: ButtonState.Pressed), new KeyboardState());
            Assert.That(label.HasSelection, Is.False, "Disabling selection drags restores ordinary pointer selection behavior.");
        }

        [Test]
        public void RichTextLabel_MapsGodotWordAndParagraphMultiClickSelection()
        {
            var label = new RichTextLabel { Size = new Vector2(160, 48), Padding = Thickness.Zero, SelectionEnabled = true };
            label.AppendText("alpha beta\ngamma delta");
            var context = new UIContext(); context.Add(label);
            var point = new Point(6, 6);
            var start = new GameTime(TimeSpan.Zero, TimeSpan.Zero);
            var doubleClick = new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.Zero);
            var tripleClick = new GameTime(TimeSpan.FromMilliseconds(200), TimeSpan.Zero);

            context.Update(start, Mouse(point.X, point.Y), new KeyboardState());
            context.Update(start, Mouse(point.X, point.Y, left: ButtonState.Pressed), new KeyboardState());
            context.Update(start, Mouse(point.X, point.Y), new KeyboardState());
            context.Update(doubleClick, Mouse(point.X, point.Y, left: ButtonState.Pressed), new KeyboardState());
            context.Update(doubleClick, Mouse(point.X, point.Y), new KeyboardState());
            Assert.That(label.GetSelectedText(), Is.EqualTo("alpha"));

            context.Update(tripleClick, Mouse(point.X, point.Y, left: ButtonState.Pressed), new KeyboardState());
            context.Update(tripleClick, Mouse(point.X, point.Y), new KeyboardState());
            Assert.That(label.GetSelectedText(), Is.EqualTo("alpha beta"));

            var delayed = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.Zero);
            context.Update(delayed, Mouse(point.X, point.Y, left: ButtonState.Pressed), new KeyboardState());
            context.Update(delayed, Mouse(point.X, point.Y), new KeyboardState());
            Assert.That(label.HasSelection, Is.False, "A click beyond Godot's multi-click timeout restarts single-click selection.");
        }

        [Test]
        public void RichTextLabel_MapsGodotFocusedSelectionShortcutsAndCopyRequest()
        {
            var label = new RichTextLabel { Size = new Vector2(160, 24), SelectionEnabled = true };
            label.AppendText("Node status");
            var copied = string.Empty;
            label.CopyRequested += (_, text) => copied = text;
            var clipboard = new TestClipboard();
            var context = new UIContext { Clipboard = clipboard }; context.Add(label); context.SetFocus(label);

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.A));
            Assert.That(label.GetSelectedText(), Is.EqualTo("Node status"));

            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.C));
            Assert.That(copied, Is.EqualTo("Node status"));
            Assert.That(clipboard.Text, Is.EqualTo("Node status"));

            label.Deselect(); label.ShortcutKeysEnabled = false;
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl));
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftControl, Keys.A));
            Assert.That(label.GetSelectedText(), Is.Empty);
        }

        [Test]
        public void RichTextLabel_MapsGodotContextMenuCopyAndSelectAllCommands()
        {
            var label = new RichTextLabel { Size = new Vector2(160, 24), SelectionEnabled = true, ContextMenuEnabled = true };
            label.AppendText("Node status");
            var copied = string.Empty;
            label.CopyRequested += (_, text) => copied = text;
            var clipboard = new TestClipboard();
            var context = new UIContext { Clipboard = clipboard }; context.Add(label);

            context.Update(Time, Mouse(4, 6), new KeyboardState());
            context.Update(Time, Mouse(4, 6, right: ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(4, 6), new KeyboardState());
            var menu = label.GetMenu();
            Assert.That(menu.Visible, Is.True);
            Assert.That(menu.Items, Has.Count.EqualTo(2));
            Assert.That(menu.Items[0].Text, Is.EqualTo("Copy"));
            Assert.That(menu.Items[1].Text, Is.EqualTo("Select All"));

            context.Update(Time, Mouse(menu.Bounds.X + 8, menu.Bounds.Y + 8, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(menu.Bounds.X + 8, menu.Bounds.Y + 8), new KeyboardState());
            Assert.That(copied, Is.EqualTo("Node status"));
            Assert.That(clipboard.Text, Is.EqualTo("Node status"));
            Assert.That(menu.Visible, Is.False);

            context.SetFocus(label);
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.Apps));
            Assert.That(menu.Visible, Is.True);
            Assert.That(menu.Position, Is.EqualTo(label.GlobalPosition));
            menu.Hide(); context.SetFocus(label);
            context.Update(Time, Mouse(0, 0), new KeyboardState());
            context.Update(Time, Mouse(0, 0), new KeyboardState(Keys.LeftShift, Keys.F10));
            Assert.That(menu.Visible, Is.True, "Shift+F10 is the conventional second ui_menu binding.");
        }

        [Test]
        public void FoldableContainer_ChangesChildVisibilityAndMinimumSize()
        {
            var foldable = new FoldableContainer { HeaderHeight = 20 };
            var child = new Control { CustomMinimumSize = new Vector2(80, 30) };
            foldable.AddChild(child);

            Assert.That(foldable.GetMinimumSize().Y, Is.EqualTo(50));
            foldable.Folded = true;
            Assert.That(child.Visible, Is.False);
            Assert.That(foldable.GetMinimumSize().Y, Is.EqualTo(20));
        }

        [Test]
        public void FoldableContainer_TogglesFoldedOnEnterAndSpaceLikeGodotUiAccept()
        {
            var foldable = new FoldableContainer { HeaderHeight = 20 };
            var changes = 0; foldable.FoldedChanged += (_, _) => changes++;

            foldable.KeyPressed(Keys.Enter);
            Assert.That(foldable.Folded, Is.True, "Godot's FoldableContainer::gui_input toggles folded on the ui_accept action.");
            Assert.That(changes, Is.EqualTo(1));

            foldable.KeyPressed(Keys.Space);
            Assert.That(foldable.Folded, Is.False, "Space also maps to ui_accept.");
            Assert.That(changes, Is.EqualTo(2));

            foldable.KeyPressed(Keys.Tab);
            Assert.That(foldable.Folded, Is.False, "Unrelated keys do not toggle folding.");
            Assert.That(changes, Is.EqualTo(2));
        }

        [Test]
        public void FoldableGroup_EnforcesGodotSingleExpansionAccordionBehavior()
        {
            var group = new FoldableGroup();
            var first = new FoldableContainer { Title = "Transform" };
            var second = new FoldableContainer { Title = "Material" };
            var third = new FoldableContainer { Title = "Physics", Folded = true };
            var expandedEvents = new List<FoldableContainer>();
            group.Expanded += (_, container) => expandedEvents.Add(container);

            first.FoldableGroup = group;
            Assert.That(first.Folded, Is.False, "The group was empty when first joined, so it simply stays expanded as-is.");
            second.FoldableGroup = group;
            Assert.That(second.Folded, Is.True, "A member joining a group that already has an expanded container is folded to preserve single expansion.");
            third.FoldableGroup = group;
            Assert.That(third.Folded, Is.True, "A container already folded when it joins stays folded.");
            Assert.That(group.GetExpandedContainer(), Is.SameAs(first));
            Assert.That(expandedEvents, Is.Empty, "Joining a group never itself fires expanded; changing_group suppresses it during the join-time correction.");

            second.Folded = false;
            Assert.That(second.Folded, Is.False);
            Assert.That(first.Folded, Is.True, "Expanding one member folds all siblings in the group.");
            Assert.That(third.Folded, Is.True);
            Assert.That(group.GetExpandedContainer(), Is.SameAs(second));
            Assert.That(expandedEvents, Is.EqualTo(new[] { second }), "Godot's FoldableGroup.expanded signal fires once for this successful expansion.");

            second.Folded = true;
            Assert.That(second.Folded, Is.False, "With allow_folding_all false, folding the group's only expanded container is refused.");
            Assert.That(group.GetExpandedContainer(), Is.SameAs(second));

            group.AllowFoldingAll = true;
            second.Folded = true;
            Assert.That(second.Folded, Is.True, "allow_folding_all permits folding the last expanded container.");
            Assert.That(group.GetExpandedContainer(), Is.Null);

            group.AllowFoldingAll = false;
            Assert.That(group.GetExpandedContainer(), Is.SameAs(first), "Disabling allow_folding_all with nothing expanded re-expands the group's first member.");
            Assert.That(expandedEvents, Is.EqualTo(new[] { second, first }), "The forced re-expand goes through the normal set_folded(false) path, so it fires expanded too.");

            first.FoldableGroup = null;
            Assert.That(group.GetContainers(), Is.EquivalentTo(new[] { second, third }), "Leaving the group removes the container from its membership.");
        }

        [Test]
        public void FoldableGroup_CascadedSiblingFoldsDoNotFireFoldedChangedLikeGodotBareSetFolded()
        {
            // Godot's FoldableContainer::fold()/expand() explicitly emit folding_changed; the exported
            // `folded` property (and FoldableGroup's sibling-fold cascade, which goes through a bare
            // set_folded call) is bound straight to set_folded, which never emits the signal itself.
            var group = new FoldableGroup();
            var first = new FoldableContainer(); var second = new FoldableContainer();
            first.FoldableGroup = group; second.FoldableGroup = group;
            var firstChanges = new List<bool>(); first.FoldedChanged += (_, folded) => firstChanges.Add(folded);
            var secondChanges = new List<bool>(); second.FoldedChanged += (_, folded) => secondChanges.Add(folded);

            second.Expand();
            Assert.That(secondChanges, Is.EqualTo(new[] { false }), "Expand() explicitly fires the signal.");
            Assert.That(firstChanges, Is.Empty, "The sibling fold cascaded through the group's bare Folded assignment must not fire the signal.");

            first.Folded = false;
            Assert.That(firstChanges, Is.Empty, "Setting the Folded property directly never fires the signal, matching Godot's bare set_folded binding.");
        }

        [Test]
        public void ScrollContainer_ConsumesWheelInputFromAChild()
        {
            // Godot's real wheel-scroll amount is always the scrollbar's own page / PAGE_DIVISOR (8),
            // not a fixed step - here the content is narrower than the container even after reserving
            // the vertical scrollbar's own width, so no horizontal scrollbar ever shows and the viewport
            // height stays exactly 80; one wheel notch then scrolls by 80/8 = 10.
            var scroll = new ScrollContainer { Size = new Vector2(100, 80) };
            scroll.AddChild(new Control { CustomMinimumSize = new Vector2(70, 400) });
            var context = new UIContext(); context.Add(scroll);
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, new MouseState(10, 10, -120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());

            Assert.That(scroll.ScrollOffset.Y, Is.EqualTo(10));
        }

        [Test]
        public void ScrollContainer_OnlyStretchesChildrenWithTheExpandSizeFlagLikeGodotsReposition()
        {
            // Godot's _reposition_children only stretches an axis to the viewport size when SIZE_EXPAND
            // is set on it; without that flag (the common case), the child keeps its own minimum size.
            var plainScroll = new ScrollContainer { Size = new Vector2(100, 80) };
            var plain = new Control { CustomMinimumSize = new Vector2(50, 30) };
            plainScroll.AddChild(plain);
            var plainContext = new UIContext(); plainContext.Add(plainScroll); plainContext.Layout();
            Assert.That(plain.Size, Is.EqualTo(new Vector2(50, 30)), "A child without SIZE_EXPAND must keep its own minimum size, not stretch to fill the viewport.");

            var expandScroll = new ScrollContainer { Size = new Vector2(100, 80) };
            var expanded = new Control { CustomMinimumSize = new Vector2(50, 30), HorizontalSizeFlags = SizeFlags.Expand | SizeFlags.Fill, VerticalSizeFlags = SizeFlags.Expand | SizeFlags.Fill };
            expandScroll.AddChild(expanded);
            var expandContext = new UIContext(); expandContext.Add(expandScroll); expandContext.Layout();
            Assert.That(expanded.Size, Is.EqualTo(new Vector2(100, 80)), "A child WITH SIZE_EXPAND still stretches to the viewport size.");
        }

        [Test]
        public void ScrollContainer_MinimumSizeReservesTheOppositeScrollbarWhenItShowsLikeGodot()
        {
            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollBarVisibility.Disabled, Size = new Vector2(100, 50) };
            scroll.AddChild(new Control { CustomMinimumSize = new Vector2(60, 200) });
            var context = new UIContext(); context.Add(scroll); context.Layout();

            var minimum = scroll.GetMinimumSize();
            Assert.That(minimum.X, Is.EqualTo(60 + scroll.VerticalScrollBar.GetMinimumSize().X), "Disabling horizontal scrolling must still budget width for the vertical scrollbar once it shows, matching Godot's _get_minimum_size.");
        }

        [Test]
        public void ScrollContainer_EnsureControlVisibleUsesAZeroMarginLikeGodot()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 100) };
            var canvas = new Control { CustomMinimumSize = new Vector2(70, 300) };
            var target = new Control { Position = new Vector2(0, 150), Size = new Vector2(10, 10) };
            canvas.AddChild(target);
            scroll.AddChild(canvas);
            var context = new UIContext(); context.Add(scroll); context.Layout();

            scroll.EnsureControlVisible(target);

            // Godot's ensure_control_visible scrolls the exact minimal amount (target.Bottom - viewport.Bottom
            // = 160 - 100 = 60) with zero margin; the old, incorrect 20px "scroll_border" margin would have
            // over-scrolled to 80 instead.
            Assert.That(scroll.ScrollOffset.Y, Is.EqualTo(60));
        }

        [Test]
        public void ScrollContainer_ProgrammaticScrollCancelsAnInProgressTouchDragLikeGodotsCancelDrag()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 80) };
            scroll.AddChild(new Control { CustomMinimumSize = new Vector2(70, 400) });
            var context = new UIContext(); context.Add(scroll); context.Layout();

            scroll.BeginTouchDragScroll();
            scroll.TouchDragScrollBy(new Vector2(0, -50));
            Assert.That(scroll.IsTouchDragging, Is.True);

            scroll.VerticalScroll = 20;
            Assert.That(scroll.IsTouchDragging, Is.False, "Setting VerticalScroll must cancel the active touch drag, matching Godot's set_v_scroll calling _cancel_drag.");
        }

        [Test]
        public void ScrollContainer_ShiftWheelSwapsToHorizontalLikeGodotsSwapAxes()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 80) };
            scroll.AddChild(new Control { CustomMinimumSize = new Vector2(300, 300) });
            var context = new UIContext(); context.Add(scroll); context.Layout();
            context.Update(Time, Mouse(10, 10), new KeyboardState(Keys.LeftShift));
            context.Update(Time, new MouseState(10, 10, -120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState(Keys.LeftShift));

            Assert.That(scroll.ScrollOffset.Y, Is.EqualTo(0), "Shift+wheel must not touch the vertical axis.");
            Assert.That(scroll.ScrollOffset.X, Is.GreaterThan(0), "Shift+wheel scrolls horizontally instead, matching Godot's swap_axes.");
        }

        [Test]
        public void ScrollContainer_WheelFallsBackToHorizontalWhenTheVerticalScrollbarIsHiddenLikeGodot()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 80), VerticalScrollMode = ScrollBarVisibility.Disabled };
            scroll.AddChild(new Control { CustomMinimumSize = new Vector2(300, 60) });
            var context = new UIContext(); context.Add(scroll); context.Layout();
            context.Update(Time, Mouse(10, 10), new KeyboardState());
            context.Update(Time, new MouseState(10, 10, -120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());

            Assert.That(scroll.ScrollOffset.X, Is.GreaterThan(0), "With the vertical scrollbar disabled, a plain wheel notch falls back to scrolling horizontally, matching Godot's v_scroll_hidden branch.");
        }

        [Test]
        public void ScrollContainer_ExposesSyncedInteractiveScrollBars()
        {
            var scroll = new ScrollContainer { Size = new Vector2(100, 100) };
            scroll.AddChild(new Control { CustomMinimumSize = new Vector2(240, 240) });
            var context = new UIContext(); context.Add(scroll); context.Layout();

            scroll.VerticalScrollBar.Value = 40;

            Assert.That(scroll.HorizontalScrollBar.Visible, Is.True);
            Assert.That(scroll.VerticalScrollBar.Visible, Is.True);
            Assert.That(scroll.ScrollOffset.Y, Is.EqualTo(40));
        }

        [Test]
        public void SpinBoxLineEdit_CommitsTextAndRoutesArrowKeysToItsOwner()
        {
            var spin = new SpinBox { Step = .5f };
            spin.SetPrefix("px");
            spin.SetSuffix("units");
            spin.SetEditable(false);
            spin.SetSelectAllOnFocus(true);
            spin.SetUpdateOnTextChanged(true);
            spin.SetCustomArrowStep(2);
            spin.SetCustomArrowRound(true);
            spin.SetHorizontalAlignment(HorizontalAlignment.Right);
            spin.LineEdit.Text = "12.5";
            spin.Apply();
            spin.LineEdit.KeyPressed(Keys.Up);
            spin.LineEdit.KeyPressed(Keys.Down);
            spin.LineEdit.Text = "px 9.25 units";
            spin.LineEdit.KeyPressed(Keys.Enter);
            spin.LineEdit.Text = "11";

            Assert.That(spin.Value, Is.EqualTo(11));
            Assert.That(spin.LineEdit.Owner, Is.SameAs(spin));
            Assert.That(spin.GetLineEdit(), Is.SameAs(spin.LineEdit));
            Assert.That(spin.GetPrefix(), Is.EqualTo("px"));
            Assert.That(spin.GetSuffix(), Is.EqualTo("units"));
            Assert.That(spin.IsEditable(), Is.False);
            Assert.That(spin.IsSelectAllOnFocus(), Is.True);
            Assert.That(spin.GetUpdateOnTextChanged(), Is.True);
            Assert.That(spin.GetCustomArrowStep(), Is.EqualTo(2));
            Assert.That(spin.IsCustomArrowRounding(), Is.True);
            Assert.That(spin.GetHorizontalAlignment(), Is.EqualTo(HorizontalAlignment.Right));
            Assert.That(spin.LineEdit.Text, Is.EqualTo("px 11 units"));
            spin.SetCustomArrowStep(-4);
            Assert.That(spin.GetCustomArrowStep(), Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => spin.SetHorizontalAlignment((HorizontalAlignment)999));
        }

        [Test]
        public void SpinBox_MapsGodotVerticalDragAdjustmentLifecycle()
        {
            var spin = new SpinBox { Size = new Vector2(100, 24), MinValue = 0, MaxValue = 100, Step = 1, Value = 50 };
            var context = new UIContext(); context.Add(spin);

            context.Update(Time, Mouse(94, 12), new KeyboardState());
            context.Update(Time, Mouse(94, 12, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.Value, Is.EqualTo(49), "The lower half still performs the initial down arrow step.");

            context.Update(Time, Mouse(94, 13, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.IsDraggingValue, Is.False);
            Assert.That(spin.Value, Is.EqualTo(49), "Godot waits for a drag threshold before relative value adjustment.");

            context.Update(Time, Mouse(94, 16, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.IsDraggingValue, Is.True);
            Assert.That(spin.Value, Is.EqualTo(49), "Crossing the threshold starts drag mode without applying a relative delta yet.");

            context.Update(Time, Mouse(94, 26, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.Value, Is.EqualTo(48));

            context.Update(Time, Mouse(94, 26), new KeyboardState());
            Assert.That(spin.IsDraggingValue, Is.False);

            spin.Value = 50;
            spin.SetEditable(false);
            context.Update(Time, Mouse(94, 12), new KeyboardState());
            context.Update(Time, Mouse(94, 12, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(94, 30, ButtonState.Pressed), new KeyboardState());

            Assert.That(spin.Value, Is.EqualTo(50));
            Assert.That(spin.IsDraggingValue, Is.False);
        }

        [Test]
        public void SpinBox_HorizontalArrowLayoutStepsFromFullHeightEndButtons()
        {
            var spin = new SpinBox
            {
                Size = new Vector2(100, 24),
                MinValue = 0,
                MaxValue = 100,
                Step = 1,
                Value = 50,
                ArrowLayout = SpinBoxArrowLayout.Horizontal,
            };
            var context = new UIContext(); context.Add(spin);

            spin.GetArrowButtonRectangles(out var decrement, out var increment);
            context.Update(Time, Mouse(8, 12, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(8, 12), new KeyboardState());
            context.Update(Time, Mouse(92, 12, ButtonState.Pressed), new KeyboardState());

            Assert.That(spin.GetArrowLayout(), Is.EqualTo(SpinBoxArrowLayout.Horizontal));
            Assert.That(decrement, Is.EqualTo(new Rectangle(0, 0, 16, 24)));
            Assert.That(increment, Is.EqualTo(new Rectangle(84, 0, 16, 24)));
            Assert.That(spin.Value, Is.EqualTo(50));
            Assert.Throws<ArgumentOutOfRangeException>(() => spin.SetArrowLayout((SpinBoxArrowLayout)999));
        }

        [Test]
        public void SpinBox_MapsGodotHeldArrowRepeatTimingAndCancellation()
        {
            var spin = new SpinBox { Size = new Vector2(100, 24), MinValue = 0, MaxValue = 100, Step = 1, Value = 10 };
            var context = new UIContext(); context.Add(spin);

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), Mouse(94, 4, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.Value, Is.EqualTo(11), "The first press immediately activates the upper arrow.");

            context.Update(new GameTime(TimeSpan.FromMilliseconds(599), TimeSpan.FromMilliseconds(599)), Mouse(94, 4, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.Value, Is.EqualTo(11), "Godot waits for the one-shot repeat delay before auto-stepping.");

            context.Update(new GameTime(TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(1)), Mouse(94, 4, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.Value, Is.EqualTo(12));

            context.Update(new GameTime(TimeSpan.FromMilliseconds(675), TimeSpan.FromMilliseconds(75)), Mouse(94, 4, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.Value, Is.EqualTo(13), "After the first timeout Godot repeats with a short fixed cadence.");

            context.Update(new GameTime(TimeSpan.FromMilliseconds(825), TimeSpan.FromMilliseconds(150)), Mouse(94, 4, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.Value, Is.EqualTo(15), "Large retained frames catch up across all elapsed repeat intervals.");

            context.Update(new GameTime(TimeSpan.FromMilliseconds(825), TimeSpan.Zero), Mouse(94, 4), new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1175)), Mouse(94, 4), new KeyboardState());
            Assert.That(spin.Value, Is.EqualTo(15), "Releasing the left button stops the repeat timer.");

            spin.Value = 50;
            context.Update(new GameTime(TimeSpan.FromSeconds(3), TimeSpan.Zero), Mouse(94, 16, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.Value, Is.EqualTo(49));

            context.Update(new GameTime(TimeSpan.FromMilliseconds(3100), TimeSpan.FromMilliseconds(100)), Mouse(94, 20, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.IsDraggingValue, Is.True);

            context.Update(new GameTime(TimeSpan.FromMilliseconds(4000), TimeSpan.FromMilliseconds(900)), Mouse(94, 20, ButtonState.Pressed), new KeyboardState());
            Assert.That(spin.Value, Is.EqualTo(49), "Starting vertical drag mode cancels held-arrow repeat.");
        }

        [Test]
        public void SplitContainer_DragsItsDivider()
        {
            var split = new HSplitContainer { Size = new Vector2(100, 30), DragAreaSize = 6 };
            split.AddChild(new Control { CustomMinimumSize = new Vector2(10, 10) });
            split.AddChild(new Control { CustomMinimumSize = new Vector2(10, 10) });
            var context = new UIContext(); context.Add(split);
            context.Update(Time, Mouse(52, 10), new KeyboardState());
            context.Update(Time, Mouse(52, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(72, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(72, 10), new KeyboardState());

            // Godot's SplitContainerDragger::gui_input tracks a relative delta from the press point
            // (new_drag_offset = start_offset + (current - drag_from)), not an absolute recompute from
            // the current pointer position: 0 + (72-52) = 20, regardless of where within the bar the
            // press landed.
            Assert.That(split.SplitOffset, Is.EqualTo(20).Within(.001));
        }

        [Test]
        public void SplitContainer_TemplateReplacementPreservesLogicalChildrenAndOwnerInput()
        {
            var split = new HSplitContainer { Size = new Vector2(120, 30), DragAreaSize = 6 };
            var first = new Control { CustomMinimumSize = new Vector2(10, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(10, 10) };
            split.AddChild(first);
            split.AddChild(second);
            var context = new UIContext();
            context.Add(split);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(first.Parent, Is.SameAs(split));
                Assert.That(first.VisualParent, Is.TypeOf<ContentPresenter>());
                Assert.That(second.VisualParent, Is.TypeOf<ContentPresenter>());
                Assert.That(typeof(SplitContainer).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), Is.Null);
            });

            split.Template = ControlTemplate.Create<SplitContainer>((_, _) => new Border
            {
                Background = new SolidColorBrush(Color.CornflowerBlue),
            });
            var third = new Control { CustomMinimumSize = new Vector2(10, 10) };
            split.AddChild(third);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(split.TemplateRoot, Is.TypeOf<Border>());
                Assert.That(first.Parent, Is.SameAs(split));
                Assert.That(first.VisualParent, Is.Null);
                Assert.That(third.Parent, Is.SameAs(split));
                Assert.That(third.VisualParent, Is.Null);
            });

            split.GrabFocus();
            context.Update(Time, Mouse(200, 200), new KeyboardState(Keys.Right));
            Assert.That(split.SplitOffset, Is.GreaterThan(0));

            split.Template = null;
            context.Layout();
            Assert.Multiple(() =>
            {
                Assert.That(first.VisualParent, Is.TypeOf<ContentPresenter>());
                Assert.That(second.VisualParent, Is.TypeOf<ContentPresenter>());
                Assert.That(third.VisualParent, Is.TypeOf<ContentPresenter>());
            });
        }

        [Test]
        public void SplitContainer_ArrangesAndDragsMultiplePersistedSplitOffsetsLikeGodot()
        {
            var split = new HSplitContainer { Size = new Vector2(300, 40), DragAreaSize = 6 };
            var first = new Control { CustomMinimumSize = new Vector2(10, 10) };
            var second = new Control { CustomMinimumSize = new Vector2(10, 10) };
            var third = new Control { CustomMinimumSize = new Vector2(10, 10) };
            split.AddChild(first); split.AddChild(second); split.AddChild(third);
            split.SetSplitOffsets(20, -20);
            var context = new UIContext(); context.Add(split); context.Layout();

            Assert.That(first.Bounds, Is.EqualTo(new Rectangle(0, 0, 116, 40)));
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(122, 0, 56, 40)));
            Assert.That(third.Bounds, Is.EqualTo(new Rectangle(184, 0, 116, 40)));
            Assert.That(split.GetSplitOffset(1), Is.EqualTo(-20));

            context.Update(Time, Mouse(180, 10), new KeyboardState());
            context.Update(Time, Mouse(180, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(200, 10, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(200, 10), new KeyboardState());
            context.Layout();

            Assert.That(split.GetSplitOffset(1), Is.EqualTo(0));
            Assert.That(second.Bounds.Width, Is.EqualTo(76));
            Assert.That(third.Bounds.Width, Is.EqualTo(96));
        }

        [Test]
        public void SplitContainer_DragsNestedOrthogonalDividerAtIntersectionLikeGodot()
        {
            var outer = new HSplitContainer { Size = new Vector2(200, 120), DragAreaSize = 6 };
            var nested = new VSplitContainer { DragAreaSize = 6, DraggingNestedIntersections = true };
            nested.AddChild(new Control { CustomMinimumSize = new Vector2(10, 10) });
            nested.AddChild(new Control { CustomMinimumSize = new Vector2(10, 10) });
            outer.AddChild(nested);
            outer.AddChild(new Control { CustomMinimumSize = new Vector2(10, 10) });
            var context = new UIContext(); context.Add(outer); context.Layout();

            Assert.That(nested.IsDraggingNestedIntersections, Is.True);
            context.Update(Time, Mouse(99, 59, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(119, 79, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(119, 79), new KeyboardState());

            Assert.That(outer.SplitOffset, Is.EqualTo(20).Within(.001));
            Assert.That(nested.SplitOffset, Is.EqualTo(20).Within(.001));
        }

        [Test]
        public void SplitContainer_TouchDraggerUsesCenteredExpandedHitTargetLikeGodot()
        {
            var split = new HSplitContainer { Size = new Vector2(100, 80), DragAreaSize = 6, TouchDraggerSize = 24 };
            split.AddChild(new Control { CustomMinimumSize = new Vector2(10, 10) });
            split.AddChild(new Control { CustomMinimumSize = new Vector2(10, 10) });
            var context = new UIContext(); context.Add(split); context.Layout();

            context.Update(Time, Mouse(60, 40, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(70, 40, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(70, 40), new KeyboardState());
            Assert.That(split.SplitOffset, Is.Zero, "The larger touch target is disabled by default.");

            split.SetTouchDraggerEnabled(true);
            Assert.That(split.IsTouchDraggerEnabled, Is.True);
            context.Update(Time, Mouse(60, 40, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(70, 40, ButtonState.Pressed), new KeyboardState());
            context.Update(Time, Mouse(70, 40), new KeyboardState());
            Assert.That(split.SplitOffset, Is.EqualTo(10).Within(.001));
        }

        private sealed class UpperCaseEffect : RichTextEffect
        {
            public override string Process(string source) => source.ToUpperInvariant();
        }

        private sealed class DesiredSizeControl : Control
        {
            private readonly Vector2 _desiredSize;
            public DesiredSizeControl(Vector2 desiredSize) => _desiredSize = desiredSize;
            public override Vector2 GetDesiredSize() => _desiredSize;
        }

        private sealed class DragSource : Control
        {
            public override object GetDragData(Point position) => "scene-node";
        }

        private sealed class DragTarget : Control
        {
            public string Received { get; private set; }
            public override bool CanDropData(Point position, object data) => data is string;
            public override void DropData(Point position, object data) => Received = (string)data;
        }

        private sealed class InputProbe : Control
        {
            public int PressedCount { get; private set; }
            internal override void PointerPressed(Point position) => PressedCount++;
        }

        private sealed class PointerButtonProbe : Control
        {
            public List<PointerButton> PressedButtons { get; } = new List<PointerButton>();
            public List<PointerButton> ReleasedButtons { get; } = new List<PointerButton>();
            internal override void PointerButtonPressed(Point position, PointerButton button) => PressedButtons.Add(button);
            internal override void PointerButtonReleased(Point position, PointerButton button) => ReleasedButtons.Add(button);
        }

        private sealed class AcceptingProbe : Control
        {
            public int PressedCount { get; private set; }
            internal override void PointerPressed(Point position) { PressedCount++; AcceptEvent(); }
        }
    }
}
