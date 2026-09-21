// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma;

using var fontStream = typeof(Program).Assembly.GetManifestResourceStream("Inter.ttf")!;
using var fontBytes = new MemoryStream();
fontStream.CopyTo(fontBytes);
using var face = UIFontFace.FromMemory(fontBytes.ToArray());
var rich = new TextBlock
{
    UIFont = new DynamicUIFont(face, 18), Size = new(200, 60), Padding = Thickness.Zero,
    VisibleCharactersBehavior = LabelVisibleCharactersBehavior.CharactersAfterShaping,
};
rich.Inlines.Add(new Run("e\u0301XYZ") { Meta = "lesson", Decoration = TextDecoration.Underline });
var fullSize = rich.GetMinimumSize();
var hit = rich.GetCharacterBounds(0).Center;
rich.VisibleCharacters = 1;
if (rich.GetMinimumSize() != fullSize || rich.GetCharacterBounds(2) != Microsoft.Xna.Framework.Rectangle.Empty ||
    !Equals(rich.GetMetaUnderPosition(hit), "lesson"))
    throw new InvalidOperationException("NativeAOT typed rich-text reveal/link contract failed.");
rich.VisibleCharacters = 0;
if (rich.GetMetaUnderPosition(hit) is not null)
    throw new InvalidOperationException("NativeAOT hidden rich-text link remained interactive.");

Control[] controls =
[
    new Label { Text = "Analyzer consumer" },
    new Button { Text = "Continue" },
    new VideoStreamPlayer(),
];

var nativeDiagnostics = DynamicTextNativeDiagnostics.Current;
var svgHealth = SvgBackendDefaults.Verify();
Console.WriteLine($"{controls.Length} controls; {VideoStreamPlayer.RuntimeCapabilities}; {nativeDiagnostics.RuntimeIdentifier}; {svgHealth.Name} {svgHealth.Version}; typed rich text: PASS");