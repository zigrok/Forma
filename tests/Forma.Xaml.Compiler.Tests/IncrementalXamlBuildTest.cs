// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Forma.Xaml.Build;
using Microsoft.Xna.Framework;
using Mono.Cecil;

namespace Forma.Xaml.Compiler.Tests;

public class IncrementalXamlBuildTest
{
    [Test]
    public async Task XamlAndImplementationOnlyDependencyEditsRebuildPristineAssemblyBeforeInjection()
    {
        var root = Path.Combine(Path.GetTempPath(), "forma-incremental-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var references = new[] { typeof(Control).Assembly, typeof(Vector2).Assembly };
            var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("TargetFramework", "net10.0"),
                    new XElement("OutputType", "Exe"),
                    new XElement("UseAppHost", "false"),
                    new XElement("NuGetAudit", "false"),
                    new XElement("RestoreSources", Path.Combine(root, "offline-feed")),
                    new XElement("FormaXamlBuildAssembly", typeof(CompileFormaXaml).Assembly.Location)),
                new XElement("ItemGroup", references.Select(assembly =>
                    new XElement("Reference", new XAttribute("Include", assembly.GetName().Name!),
                        new XElement("HintPath", assembly.Location)))),
                new XElement("ItemGroup",
                    new XElement("Compile", new XAttribute("Remove", "Dependency/**/*.cs")),
                    new XElement("ProjectReference", new XAttribute("Include", "Dependency/Dependency.csproj"))),
                new XElement("Import", new XAttribute("Project", TargetsPath())));
            Directory.CreateDirectory(Path.Combine(root, "offline-feed"));
            Directory.CreateDirectory(Path.Combine(root, "Dependency"));
            await File.WriteAllTextAsync(Path.Combine(root, "Dependency", "Dependency.csproj"),
                new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup",
                        new XElement("TargetFramework", "net10.0"),
                        new XElement("NuGetAudit", "false"),
                        new XElement("RestoreSources", Path.Combine(root, "offline-feed")))).ToString());
            var dependencySource = Path.Combine(root, "Dependency", "Dependency.cs");
            await File.WriteAllTextAsync(dependencySource, "public static class Dependency { public static int Value() => 1; }");
            await File.WriteAllTextAsync(Path.Combine(root, "Probe.csproj"), project.ToString());
            await File.WriteAllTextAsync(Path.Combine(root, "Probe.cs"), """
                using System;
                using Forma;
                using Forma.Xaml;
                namespace IncrementalProbe;
                public sealed class View : Control
                {
                    public View() => FormaXamlLoader.Load(this);
                }
                public static class Program
                {
                    public static void Main() => Console.WriteLine(new View().Size.X);
                }
                """);
            var xaml = Path.Combine(root, "View.xaml");
            string Source(int width) => $"""
                <Control xmlns="https://forma.dev/xaml"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="IncrementalProbe.View" Size="{width},42" />
                """;
            var output = Path.Combine(root, "bin", "Release", "net10.0", "Probe.dll");
            async Task Build() => _ = await Run(root, "build", "Probe.csproj", "-c", "Release",
                "--disable-build-servers", "-m:1", "-p:BuildInParallel=false", "-p:UseSharedCompilation=false");
            await File.WriteAllTextAsync(xaml, Source(100));
            await Build();
            Assert.That((await Run(root, output)).Trim(), Is.EqualTo("100"));
            await File.WriteAllTextAsync(xaml, Source(200));
            await Build();
            Assert.That((await Run(root, output)).Trim(), Is.EqualTo("200"));
            await Build();
            Assert.That((await Run(root, output)).Trim(), Is.EqualTo("200"));
            using (var emitted = AssemblyDefinition.ReadAssembly(output))
                Assert.That(emitted.MainModule.Types.Where(type => type.Namespace == "Forma.Xaml.Generated")
                    .GroupBy(type => type.FullName).All(group => group.Count() == 1), Is.True);
            var referenceAssembly = Path.Combine(root, "Dependency", "obj", "Release", "net10.0", "ref", "Dependency.dll");
            var previousReference = await File.ReadAllBytesAsync(referenceAssembly);
            await File.WriteAllTextAsync(dependencySource, "public static class Dependency { public static int Value() => 2; }");
            await Build();
            Assert.That(await File.ReadAllBytesAsync(referenceAssembly), Is.EqualTo(previousReference),
                "This regression requires an implementation-only change with an unchanged public reference assembly.");
            using (var afterDependency = AssemblyDefinition.ReadAssembly(output))
                Assert.That(afterDependency.MainModule.Types.Where(type => type.Namespace == "Forma.Xaml.Generated")
                    .GroupBy(type => type.FullName).All(group => group.Count() == 1), Is.True,
                    "Changed implementation references must not reinject into an already woven assembly.");
            Assert.That((await Run(root, output)).Trim(), Is.EqualTo("200"));
            File.Delete(xaml);
            await Build();
            using (var withoutXaml = AssemblyDefinition.ReadAssembly(output))
                Assert.That(withoutXaml.MainModule.Types.Any(type => type.Namespace == "Forma.Xaml.Generated"), Is.False);
            await File.WriteAllTextAsync(xaml, Source(300));
            await Build();
            Assert.That((await Run(root, output)).Trim(), Is.EqualTo("300"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string TargetsPath([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../src/Forma.Xaml.Build/buildTransitive/Forma.Xaml.Build.targets"));

    private static async Task<string> Run(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated XAML build.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("The isolated incremental XAML build exceeded 90 seconds.");
        }
        var output = await stdout;
        Assert.That(process.ExitCode, Is.Zero, output + await stderr);
        return output;
    }
}
