# Headless UI testing

`UIContext` needs no window or `GraphicsDevice` for layout, hit testing and input, so control behavior can be tested in
plain NUnit/xUnit. `Forma.Testing` adds `HeadlessInput`, a small driver over the context's `Inject*` side channel.

```csharp
var input = HeadlessInput.Create(800, 600);      // UIContext with SuppressPolledInput on
input.Context.Add(myList);
input.Context.Layout();

input.Click(myList.GetRealizedContainer(1));     // move + press + release at the control's centre
input.DoubleClick(row);
input.Wheel(-120, position);
input.Key(Keys.A, Keys.LeftControl);             // chord
input.Type("hello");                             // committed text
input.Gamepad(HeadlessGamepadButton.DPadDown);   // maps to the navigation key
input.Advance(TimeSpan.FromMilliseconds(100));   // run a frame (animations, timers)
```

Notes:

- Positions are physical pixels, equal to logical pixels at the default `DisplayScale` of 1.
- Gamepad input is expressed as the keys UI navigation uses (D-pad = arrows, Accept = Enter, Cancel = Escape);
  `UIContext` has no gamepad-specific entry point.
- Inject calls run synchronously on the calling thread; call them between frames.
- For game-level tests, drive the same context the game's screens read, and make the game's own input (for example a
  mouse snapshot) come from one source, or a test can pass while real input fails.
- Time-based behavior (double-click window) follows `UIContext.CurrentTime`; use `Advance` rather than sleeping.
- Rendering (`Draw`, render targets) still needs a graphics device. A game built on a MonoGame fork with a headless
  backend can run `SDL_VIDEODRIVER=dummy SDL_AUDIODRIVER=dummy` set in the *process* environment (setting them
  inside the test is too late on Unix).

## Render tests on macOS

`Forma.RenderTests` excludes macOS because it uses the stock DesktopGL package, which needs a real GL window. The
headless backend lives in a MonoGame fork; Forma can use it only through `MonoGameProjectPath`, and then only for
tests that do not read back pixels. These tests were not re-enabled.
