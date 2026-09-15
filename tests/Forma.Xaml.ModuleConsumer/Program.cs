using System.Reflection;
using Forma;
using Forma.Xaml;

if (args.Length != 1)
    throw new ArgumentException("Pass the separately built Forma.Xaml.Build.Integration.dll path.");

// The host never references the plugin or desktop compiler at build time.
var module = Assembly.Load(File.ReadAllBytes(args[0]));
var viewType = module.GetType("Forma.Xaml.Build.Integration.HudView", throwOnError: true)!;
var view = (Control)Activator.CreateInstance(viewType)!;
if (NameScope.GetNameScope(view)?.Find<Label>("Child") is null)
    throw new InvalidOperationException("The plugin-owned constructor did not populate compiled XAML.");

var result = (int)module.EntryPoint!.Invoke(null, null)!;
if (result != 0)
    throw new InvalidOperationException($"Compiled XAML integration assertions failed: {result}.");

foreach (var name in new[] { "Forma", "MonoGame.Framework" })
{
    if (AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == name) != 1)
        throw new InvalidOperationException($"The module did not share the host's {name} identity.");
}
if (AppDomain.CurrentDomain.GetAssemblies().Any(a =>
        a.GetName().Name is "Forma.Xaml.Compiler" or "Forma.Xaml.Build" or "XamlX" or "Mono.Cecil"))
    throw new InvalidOperationException("Desktop XAML tooling leaked into the runtime.");

Console.WriteLine("PASS: separately loaded executable XAML module, full integration assertions, shared runtime identity, no compiler.");
