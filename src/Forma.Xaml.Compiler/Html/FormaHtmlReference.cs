// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;

namespace Forma.Xaml.Compiler.Html;

/// <summary>A CSS property of the dialect: whether it is supported, an example declaration that the converter accepts (or the
/// reason it rejects), checked by a test so the generated reference cannot drift from the converter.</summary>
public sealed record FormaHtmlPropertyInfo(string Name, bool Supported, string Example, string Note);

/// <summary>A tested recipe: a small view for a common screen shape, converted by a test on every build.</summary>
public sealed record FormaHtmlRecipe(string Name, string Description, string Html);

/// <summary>
/// The dialect reference, generated from the same catalog the converter tests use: support matrix, reference with examples, editor
/// custom data for HTML and CSS, and the cookbook. <c>forma-xaml docs --out dir</c> writes them; a test compares the committed copies.
/// </summary>
public static class FormaHtmlReference
{
    public static IReadOnlyList<FormaHtmlPropertyInfo> Properties { get; } =
    [
        new("display", true, "display: flex", "flex, grid, block or none"),
        new("flex-direction", true, "display: flex; flex-direction: column", "row or column"),
        new("flex-wrap", true, "display: flex; flex-wrap: wrap", "nowrap or wrap (flow container)"),
        new("gap", true, "display: flex; gap: 8px", "flex containers and grids"),
        new("justify-content", true, "display: flex; justify-content: center", "start, center, end"),
        new("align-items", true, "display: flex; align-items: center", "start, center, end, stretch"),
        new("align-self", true, "align-self: center", "start, center, end, stretch"),
        new("flex-grow", true, "flex-grow: 1", "needs a flex parent"),
        new("flex", true, "flex: 1", "1, auto, 0, none"),
        new("vertical-align", true, "vertical-align: middle", "top, middle, bottom"),
        new("text-align", true, "text-align: center", "left, center, right"),
        new("width", true, "width: 120px", "fixed size (minimum and maximum)"),
        new("height", true, "height: 40px", "fixed size (minimum and maximum)"),
        new("min-width", true, "min-width: 120px", "px or rem"),
        new("min-height", true, "min-height: 40px", "px or rem"),
        new("max-width", true, "max-width: 300px", "px or rem"),
        new("max-height", true, "max-height: 300px", "px or rem"),
        new("margin", true, "margin: 4px 8px", "one to four lengths; margin-top/right/bottom/left too"),
        new("padding", true, "padding: 4px 8px", "on a flex or grid element it wraps the container in a Border"),
        new("border-width", true, "border-width: 1px", "one to four lengths"),
        new("border-color", true, "border-color: #33CDFF", "#RRGGBB or #RRGGBBAA"),
        new("border-radius", true, "border-radius: 3px", "one to four radii"),
        new("background-color", true, "background-color: #112233", "#RRGGBB or #RRGGBBAA"),
        new("background", true, "background: linear-gradient(180deg, #000000, #FFFFFF)", "linear-gradient and radial-gradient"),
        new("color", true, "color: #E0F8FF", "text color"),
        new("font-size", true, "font-size: 24px", "px or rem"),
        new("font-weight", true, "font-weight: bold", "normal, bold, 400, 700"),
        new("font-style", true, "font-style: italic", "normal, italic, oblique"),
        new("opacity", true, "opacity: 0.5", "0 to 1"),
        new("overflow", true, "overflow: hidden", "hidden clips; wrap in f-scroll to scroll"),
        new("box-shadow", true, "box-shadow: 0 2px 4px #80000000", "x y [blur [spread]] color [inset]"),
        new("text-shadow", true, "text-shadow: 1px 1px #000000", "x y color (blur is not drawn)"),
        new("text-transform", true, "text-transform: uppercase", "uppercase or none"),
        new("text-overflow", true, "text-overflow: ellipsis", "ellipsis or clip"),
        new("white-space", true, "white-space: nowrap", "nowrap or normal"),
        new("transform", true, "transform: scale(1.1)", "translate(), scale(), rotate()"),
        new("transform-origin", true, "transform-origin: 50% 50%", "keywords and percentages"),
        new("pointer-events", true, "pointer-events: none", "none or auto"),
        new("transition", true, "transition: opacity 150ms", "property and duration"),
        new("box-sizing", true, "box-sizing: border-box", "border-box only"),
        new("grid-template-columns", true, "display: grid; grid-template-columns: 1fr 2fr", "px and fr tracks"),
        new("position", false, "position: absolute", "no overlay mapping; use <dialog> or an OverlayPanel"),
        new("float", false, "float: left", "use display: flex"),
        new("filter", false, "filter: blur(2px)", "the renderer has no filters; use opacity"),
        new("backdrop-filter", false, "backdrop-filter: blur(2px)", "the renderer has no filters"),
        new("outline", false, "outline: 1px solid #000000", "use border-width and border-color"),
        new("mix-blend-mode", false, "mix-blend-mode: multiply", "no blending; use opacity"),
        new("clip-path", false, "clip-path: circle(50%)", "use overflow: hidden"),
        new("line-height", false, "line-height: 1.4", "size the box with min-height"),
        new("letter-spacing", false, "letter-spacing: 1px", "the text engine has no tracking control"),
        new("text-decoration", false, "text-decoration: underline", "the text engine has no decoration"),
        new("cursor", false, "cursor: pointer", "cursors are set by the control"),
        new("font-family", false, "font-family: Arial", "faces come from the theme"),
        new("flex-shrink", false, "flex-shrink: 1", "boxes never shrink below their minimum size"),
        new("flex-basis", false, "flex-basis: 10px", "set min-width or min-height"),
        new("order", false, "order: 1", "reorder the markup"),
        new("margin-inline-start", false, "margin-inline-start: 4px", "use margin-left or margin-right"),
    ];

    public static IReadOnlyList<FormaHtmlRecipe> Cookbook { get; } =
    [
        new("Menu", "A titled panel of full-width buttons.",
            "<div class=\"panel\" style=\"border-width: 1px; padding: 16px 18px; border-radius: 3px; min-width: 320px\"><div style=\"display: flex; flex-direction: column; gap: 10px\"><span style=\"align-self: center\">Menu</span><button style=\"min-height: 46px\">Play</button><button style=\"min-height: 46px\">Quit</button></div></div>"),
        new("Settings row", "A label, a flexible gap and a control on one row.",
            "<div style=\"display: flex; gap: 8px; min-height: 50px\"><span style=\"min-width: 190px; align-self: center\">Volume</span><div style=\"flex-grow: 1; pointer-events: none\"></div><input type=\"range\" min=\"0\" max=\"100\" style=\"min-width: 300px\"></div>"),
        new("Form", "Labelled text fields with a submit button.",
            "<div style=\"display: flex; flex-direction: column; gap: 6px\"><span>Name</span><input type=\"text\" style=\"min-width: 280px\"><span>Server</span><input type=\"text\" style=\"min-width: 280px\"><input type=\"checkbox\" checked><button>Save</button></div>"),
        new("Scrolling page", "A fixed-size scroll area with a column of rows.",
            "<f-scroll data-vertical=\"Auto\" style=\"min-width: 400px; min-height: 200px\"><div style=\"display: flex; flex-direction: column; gap: 6px; margin: 6px\"><span>One</span><span>Two</span><span>Three</span></div></f-scroll>"),
        new("Tabs", "ARIA tabs: each tabpanel becomes a tab titled by its id.",
            "<div role=\"tablist\" style=\"min-width: 300px; min-height: 200px\"><div role=\"tabpanel\" id=\"General\"><span>General</span></div><div role=\"tabpanel\" id=\"Sound\"><span>Sound</span></div></div>"),
        new("Card with effects", "A bordered surface with a gradient, shadow and rounded corners.",
            "<f-border style=\"border-width: 1px; border-radius: 4px; padding: 8px; box-shadow: 0 2px 6px #80000000; background: linear-gradient(180deg, #112233, #223344)\"><span>Card</span></f-border>"),
        new("Dialog buttons", "Centered action buttons under a message.",
            "<div style=\"display: flex; flex-direction: column; gap: 10px\"><span style=\"align-self: center\">Discard changes?</span><div style=\"display: flex; gap: 8px; align-self: center\"><button style=\"min-width: 150px\">Discard</button><button style=\"min-width: 150px\">Keep</button></div></div>"),
    ];

    /// <summary>The support matrix: every element, property, selector and at-rule with its status and the reason when rejected.</summary>
    public static string SupportMatrix()
    {
        var text = new StringBuilder();
        text.AppendLine("# HTML and CSS dialect support matrix").AppendLine();
        text.AppendLine("Generated by `forma-xaml docs`; do not edit. Status: supported, or rejected with the nearest supported alternative.").AppendLine();
        text.AppendLine("## Elements and constructs").AppendLine().AppendLine("| Construct | Lowers to |").AppendLine("| --- | --- |");
        foreach (var entry in FormaHtmlDialect.Catalog) text.AppendLine($"| {Escape(entry.Name)} | `{entry.FormaType}` |");
        text.AppendLine().AppendLine("## CSS properties").AppendLine().AppendLine("| Property | Status | Example or alternative |").AppendLine("| --- | --- | --- |");
        foreach (var property in Properties) text.AppendLine($"| `{property.Name}` | {(property.Supported ? "supported" : "rejected")} | `{property.Example}` — {Escape(property.Note)} |");
        text.AppendLine().AppendLine("## Selectors").AppendLine();
        foreach (var line in new[]
        {
            "- Type, class, id, descendant and child (`>`) combinators, `:is()`, `:where()`, `:not()`; sibling combinators are rejected.",
            "- Pseudo-classes: `:hover`, `:focus`, `:focus-visible`, `:focus-within`, `:active`, `:disabled`, `:checked`, `:selected`, `:first-child`, `:last-child`, `:only-child`, `:empty`, `:nth-child(an+b)`, `:host`.",
            "- Attributes: `[data-*]`, `[disabled]`, `[checked]`, `[type=checkbox|range|text]`, `[role=tablist]`.",
            "- Template parts: `::part(name)`. `ul.x li` and `table.x tr` select realized items. Other pseudo-elements are rejected.",
        }) text.AppendLine(line);
        text.AppendLine().AppendLine("## At-rules").AppendLine();
        text.AppendLine("- `@media (input-modality: …)`, `(prefers-reduced-motion: reduce)`, `(prefers-color-scheme: light|dark)`, `(min-width|max-width: Npx)`; `@keyframes`.");
        text.AppendLine("- `@import`, `@font-face`, `@supports`, `@container` and `!important` are rejected.");
        text.AppendLine().AppendLine("## Units").AppendLine().AppendLine("`px`, `rem`, plain numbers, `fr` in grid tracks, `ms` and `s` in animations. Percentages, `em`, `vw` and `vh` are rejected.");
        text.AppendLine().AppendLine("## Diagnostics").AppendLine().AppendLine("Every error has a stable code, a source location and a help line.").AppendLine();
        foreach (var (code, name) in DiagnosticCodes) text.AppendLine($"- `{code}` {name}");
        return text.ToString();
    }

    private static readonly (string Code, string Name)[] DiagnosticCodes =
    [
        (FormaHtmlDiagnosticCodes.Syntax, "syntax error"),
        (FormaHtmlDiagnosticCodes.UnknownElement, "unknown element"),
        (FormaHtmlDiagnosticCodes.UnknownAttribute, "unknown attribute"),
        (FormaHtmlDiagnosticCodes.RejectedConstruct, "rejected construct"),
        (FormaHtmlDiagnosticCodes.Structure, "structure error"),
        (FormaHtmlDiagnosticCodes.LinkTarget, "link target problem"),
        (FormaHtmlDiagnosticCodes.UnknownProperty, "unknown CSS property"),
        (FormaHtmlDiagnosticCodes.UnsupportedUnit, "unsupported unit"),
        (FormaHtmlDiagnosticCodes.InvalidValue, "invalid value"),
        (FormaHtmlDiagnosticCodes.UnsupportedSelector, "unsupported selector"),
        (FormaHtmlDiagnosticCodes.RejectedProperty, "rejected property"),
    ];

    /// <summary>The reference: each catalog entry with its example view and the Forma type it becomes.</summary>
    public static string Reference()
    {
        var text = new StringBuilder();
        text.AppendLine("# HTML and CSS dialect reference").AppendLine();
        text.AppendLine("Generated by `forma-xaml docs` from the catalog the converter tests compile; do not edit.").AppendLine();
        foreach (var entry in FormaHtmlDialect.Catalog)
        {
            text.AppendLine($"## {Escape(entry.Name)}").AppendLine().AppendLine($"Lowers to `{entry.FormaType}`.").AppendLine().AppendLine("```html").AppendLine(entry.Html).AppendLine("```").AppendLine();
        }

        text.AppendLine("## CSS properties").AppendLine();
        foreach (var property in Properties.Where(p => p.Supported)) text.AppendLine($"- `{property.Name}`: `{property.Example}` ({Escape(property.Note)})");
        return text.ToString();
    }

    /// <summary>The cookbook of tested recipes.</summary>
    public static string CookbookPage()
    {
        var text = new StringBuilder();
        text.AppendLine("# HTML and CSS cookbook").AppendLine();
        text.AppendLine("Generated by `forma-xaml docs`; each recipe is converted by a test on every build.").AppendLine();
        foreach (var recipe in Cookbook) text.AppendLine($"## {recipe.Name}").AppendLine().AppendLine(recipe.Description).AppendLine().AppendLine("```html").AppendLine(recipe.Html).AppendLine("```").AppendLine();
        return text.ToString();
    }

    /// <summary>VS Code custom data for HTML: the dialect's elements and attributes with descriptions, for completion and hover.</summary>
    public static string HtmlCustomData()
    {
        var elements = new[] { "div", "section", "nav", "main", "header", "footer", "span", "p", "label", "h1", "h2", "h3", "h4", "h5", "h6", "button", "input", "textarea", "select", "ul", "ol", "li", "table", "thead", "tbody", "tr", "td", "th", "progress", "details", "summary", "hr", "img", "dialog", "template", "slot", "f-border", "f-scroll", "f-group-box", "f-color-rect", "f-control" };
        var attributes = new (string Name, string Description)[]
        {
            ("id", "Names the control (x:Name)."), ("class", "Style classes."), ("part", "Marks a template part for ::part()."),
            ("bind:text", "Binds text to a view-model path; ';mode=TwoWay' for two-way."), ("bind:visible", "Binds visibility."), ("bind:enabled", "Binds enabled."),
            ("bind:items", "Binds an items source to a list or table."), ("onclick", "Code-behind handler for Pressed."), ("onchange", "Code-behind handler for Changed or TextChanged."),
            ("onitemactivated", "Code-behind handler for ItemActivated."), ("data-class", "The code-behind class of the root element."), ("data-type", "The view-model type (x:DataType)."),
            ("data-i18n", "Localization key applied by Localization.Apply."), ("data-automation-id", "Stable automation id."), ("tabindex", "0 focusable, -1 not focusable."),
            ("aria-label", "Accessible name."), ("role", "tablist or tabpanel for tabs."), ("dir", "ltr, rtl or auto."), ("lang", "Language tag, stored as data."),
            ("template", "Applies a <template for> control template by id."), ("selectable", "Makes a bound list a ListBox."), ("backdrop", "A dialog's scrim class."),
        };
        var data = new
        {
            version = 1.1,
            tags = elements.Select(name => new { name, description = "Forma dialect element. See html-css-support-matrix.md." }),
            globalAttributes = attributes.Select(a => new { name = a.Name, description = a.Description }),
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>VS Code custom data for CSS: supported properties with descriptions, plus the -f- escape hatch.</summary>
    public static string CssCustomData()
    {
        var properties = Properties.Where(p => p.Supported).Select(p => new { name = p.Name, description = p.Note + ". Example: " + p.Example })
            .Append(new { name = "-f-*", description = "Sets any Forma property verbatim: -f-FontFamily: resource(Key)." })
            .Append(new { name = "--*", description = "A custom property in :root is a theme token and the resource key itself." });
        var data = new
        {
            version = 1.1,
            properties,
            pseudoClasses = new[] { "hover", "focus", "focus-visible", "focus-within", "active", "disabled", "checked", "selected", "first-child", "last-child", "only-child", "empty", "nth-child()", "host", "is()", "where()", "not()" }.Select(name => new { name = ":" + name, description = "Supported pseudo-class." }),
            pseudoElements = new[] { new { name = "::part()", description = "Selects a template part marked with the part attribute." } },
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>The generated files by relative path.</summary>
    public static IReadOnlyDictionary<string, string> Files() => new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["html-css-support-matrix.md"] = SupportMatrix(),
        ["html-css-reference.md"] = Reference(),
        ["html-css-cookbook.md"] = CookbookPage(),
        ["editor/html.customData.json"] = HtmlCustomData(),
        ["editor/css.customData.json"] = CssCustomData(),
    };

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
