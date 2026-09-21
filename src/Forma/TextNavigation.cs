// SPDX-License-Identifier: MIT

using System.Globalization;
using Microsoft.Xna.Framework.Input;

namespace Forma;

internal enum TextNavigationAction
{
    None, PreviousCharacter, NextCharacter, PreviousWord, NextWord,
    LineStart, LineEnd, DocumentStart, DocumentEnd,
    PreviousRow, NextRow, PreviousParagraph, NextParagraph
}

internal static class TextNavigation
{
    internal static bool HasCommand(KeyboardState keyboard) =>
        keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);

    internal static bool HasControl(KeyboardState keyboard) =>
        keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);

    internal static TextNavigationAction Resolve(Keys key, KeyboardState keyboard, bool mac, bool multiline)
    {
        var command = HasCommand(keyboard);
        var control = HasControl(keyboard);
        var word = mac
            ? keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt)
            : control || command;
        return key switch
        {
            Keys.Left when mac && command => TextNavigationAction.LineStart,
            Keys.Right when mac && command => TextNavigationAction.LineEnd,
            Keys.Up when mac && command => TextNavigationAction.DocumentStart,
            Keys.Down when mac && command => TextNavigationAction.DocumentEnd,
            Keys.Left when word => TextNavigationAction.PreviousWord,
            Keys.Right when word => TextNavigationAction.NextWord,
            Keys.Up when mac && word => TextNavigationAction.PreviousParagraph,
            Keys.Down when mac && word => TextNavigationAction.NextParagraph,
            Keys.Left when !mac || !control => TextNavigationAction.PreviousCharacter,
            Keys.Right when !mac || !control => TextNavigationAction.NextCharacter,
            Keys.Home when (mac ? command : control) => TextNavigationAction.DocumentStart,
            Keys.End when (mac ? command : control) => TextNavigationAction.DocumentEnd,
            Keys.Home => TextNavigationAction.LineStart,
            Keys.End => TextNavigationAction.LineEnd,
            Keys.Up when multiline && (!mac || !control) => TextNavigationAction.PreviousRow,
            Keys.Down when multiline && (!mac || !control) => TextNavigationAction.NextRow,
            _ => TextNavigationAction.None
        };
    }

    internal static int GraphemeBoundary(string text, int from, int direction)
    {
        var previous = 0;
        foreach (var boundary in StringInfo.ParseCombiningCharacters(text))
        {
            if (direction < 0 && boundary >= from) return previous;
            if (direction > 0 && boundary > from) return boundary;
            previous = boundary;
        }
        return direction < 0 ? previous : text.Length;
    }
}
