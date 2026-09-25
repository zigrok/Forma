# Testing Forma applications

`Forma.Testing` drives a Forma UI by **role, name and automation id** instead of pixel coordinates,
in the style of Playwright. It runs headless — `new UIContext()` needs no `GraphicsDevice` — so an
app's UI tests run in CI with no window and no GPU.

## Why not coordinates

Driving a self-drawn UI by coordinate means deriving them from a screenshot and re-deriving them
whenever layout changes, with no failure when they drift — the click simply lands somewhere else.
A locator refers to *what* an element is, so it survives layout changes and says something useful
when it cannot find its target.

## A first test

```csharp
using var context = new UIContext { ViewportSize = new Vector2(900, 700) };
context.Add(BuildMyUi());

context.ByAutomationId("field.name").Fill("Ada Lovelace");
context.ByRole(AccessibilityRole.Button).ByName("Submit").Click();

UiAssert.HasValue(context.ByAutomationId("field.name"), "Ada Lovelace");
```

No `Layout()` call, no frame count. Every resolution settles the UI first.

## Finding elements

| Locator | Matches |
| --- | --- |
| `ByRole(role)` | the accessibility role |
| `ByName(name, exact: true)` | the accessible name — what a screen reader announces |
| `ByAutomationId(id)` | the author-assigned id, stable across relabelling |
| `ByText(text, exact: false)` | name or value, substring and case-insensitive by default |
| `Nth(i)` / `First` / `Last` | one of several matches; negative counts from the end |
| `Within()` | restricts the search to descendants of the current match |

Locators are **lazy**. Building one resolves nothing; it re-resolves when used. A locator created
before an element exists works once it appears, and never holds a stale reference to something that
has been recycled or re-laid-out.

Prefer `AutomationId` for anything a test binds to. `Name` doubles as the accessible name, so it
changes when a control is relabelled or localized — an automation id is a contract that does not.

## Acting

`Click()`, `Fill(text)`, `SetValue(value)`, `Focus()`, `Select()`, `Increment()`, `Decrement()`.

Every action routes through the same code real input runs, so an invoked press is the same event
sequence as a click rather than a parallel implementation that happens to raise the right event.

Actions are **gated by actionability** first. An element must be present, enabled, on screen,
non-empty, and actually hit-testable at its own bounds:

| Failure | Meaning |
| --- | --- |
| `NotPresent` | no match, or behind a modal that makes it unreachable |
| `Disabled` | the control is disabled |
| `Offscreen` | scrolled out of view — bring it into view first |
| `ZeroSized` | no area, so there is nowhere to act |
| `Obscured` | something else is on top at that point |

`Obscured` is the one no state flag reports: it comes from hit-testing the element's own centre and
confirming what comes back is the element or something inside it. Without that check a test can
"click" a button covered by an overlay and pass, which is worse than having no test.

## Asserting

`UiAssert` throws rather than depending on NUnit, xUnit or MSTest, so the same package works
whichever a project uses: `IsVisible`, `IsNotVisible`, `IsEnabled`, `IsFocused`, `HasText`,
`HasValue`, `HasCount`, `IsActionable`.

Every failure carries the accessibility tree as it stood. A UI test that fails with
"expected true but was false" forces you to reproduce it locally; one that prints the tree usually
does not.

## Waiting

`UIContext.IsSettled` reports whether the UI has stopped moving — every rendered control has had its
layout pass and no frame-boundary work is outstanding — and `WaitForSettled(maxPasses)` pumps layout
until it has. Locators call it before resolving *and* before acting.

The budget is **passes, not wall-clock time**, on purpose: a time budget behaves differently on a
loaded machine, which is exactly the flakiness this removes.

### When it never settles

`Quiescence.Describe(context)` names the controls still owing a layout pass, deepest first, as
`Type#AutomationId` paths. `Quiescence.UnsettledControls(context)` returns the same list, and
`Quiescence.HasPendingFrameWork(context)` says whether frame-boundary callbacks rather than layout
are what is keeping it busy — different causes, different fixes.

Use it in the failure message of any wait you write. "The UI never settled" tells a caller nothing;
"`ScrollContainer > ScrollContainerChromePresenter` is still dirty" points straight at the control
that queued the pass.

The usual cause is a control writing a property during its own layout without checking whether the
value changed. `QueueLayout` walks up to the root, and `LayoutTree` clears a control's flag *before*
laying out its children, so one such write re-dirties every ancestor after they have cleared — and
the tree never settles again. Idempotent setters are not a style preference here; they are what
makes auto-waiting work at all.

## Authoring rules for app developers

- Give anything a test targets an `AutomationId`.
- Give controls meaningful names; the accessible name is what `ByName` and `ByText` match.
- Prefer `ByRole` + `ByName` over `ByText` when both work — it is closer to how a person describes
  the element.

## Extending it for your own app

`Forma.Testing` stops at roles, names and bounds. It deliberately does not know what a "mesh vertex"
or a "timeline keyframe" is — and it should not. An app adds its own vocabulary as **extension
methods** over `Locator.Where`, the same primitive every built-in filter uses:

```csharp
internal static class MyAppLocators
{
    public static Locator ByTestRegion(this Locator locator, string region) =>
        locator.Where($"region={region}", node => node.AutomationId.StartsWith($"{region}."));
}
```

These compose with the built-ins rather than living in a parallel API:

```csharp
context.Query().ByTestRegion("inspector").ByRole(AccessibilityRole.Button).Click();
```

The description string matters — it appears in failure messages, so a custom filter explains itself
when it fails to match.

For an app with a surface Forma cannot see into — a rendering canvas, say — the same pattern applies:
resolve the app-specific entity to a point yourself, then drive Forma's injection primitives. Do not
build a second input path; the value of routing through the real one is that the test exercises what
a person would.
