// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

namespace Forma.Xaml.Compiler.Tests;

public sealed class AuthoringSourceIdentityTest
{
    [Test]
    public void ExplicitRootProducesPortableIdentityWithoutTemporaryDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "authoring-project");
        Assert.That(AuthoringSourceIdentity.RelativeDocument(root, Path.Combine(root, "game", "UI", "My view.xaml")),
            Is.EqualTo("game/UI/My view.xaml"));
    }

    [TestCase("../outside.xaml")]
    [TestCase("../authoring-project-other/view.xaml")]
    [TestCase(".")]
    public void OutsideRootAndRootItselfCannotBecomeDocuments(string relative)
    {
        var root = Path.Combine(Path.GetTempPath(), "authoring-project");
        Assert.Throws<ArgumentException>(() => AuthoringSourceIdentity.RelativeDocument(root, Path.GetFullPath(Path.Combine(root, relative))));
    }

    [Test]
    public void ImplicitWorkingDirectoryIsRejected()
    {
        Assert.Throws<ArgumentException>(() => AuthoringSourceIdentity.RelativeDocument("project", "/tmp/view.xaml"));
        Assert.Throws<ArgumentException>(() => AuthoringSourceIdentity.RelativeDocument(Path.GetTempPath(), "view.xaml"));
    }
}
