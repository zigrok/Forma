// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

namespace Forma.Xaml.Compiler;

public static class AuthoringSourceIdentity
{
    public static string RelativeDocument(string sourceRoot, string sourcePath)
    {
        if (!Path.IsPathFullyQualified(sourceRoot) || !Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("Authoring source root and source path must be explicit absolute paths.");
        var relative = Path.GetRelativePath(sourceRoot, sourcePath).Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            throw new ArgumentException("An authoring XAML input is outside the declared source root.");
        return relative;
    }
}
