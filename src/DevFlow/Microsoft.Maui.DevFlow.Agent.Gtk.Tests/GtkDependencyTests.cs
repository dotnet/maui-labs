using System.Text.Json;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.DevFlow.Agent.Gtk.Tests;

public class GtkDependencyTests
{
    [Fact]
    public void GtkAgent_StartupHookUsesInRepoBackend()
    {
        var hook = typeof(GtkAgentServiceExtensions).GetMethod(
            nameof(GtkAgentServiceExtensions.StartDevFlowAgent), [typeof(GtkMauiApplication)]);

        Assert.NotNull(hook);
        Assert.Equal("Microsoft.Maui.Platforms.Linux.Gtk4",
            hook.GetParameters()[0].ParameterType.Assembly.GetName().Name);
        Assert.Contains(typeof(GtkAgentServiceExtensions).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "Microsoft.Maui.Platforms.Linux.Gtk4");
    }

    [Fact]
    public void GtkAgentAndBlazor_ResolvedDependencyClosure_ContainsOnlyInRepoBackend()
    {
        var depsPath = Path.ChangeExtension(typeof(GtkDependencyTests).Assembly.Location, ".deps.json");
        using var deps = JsonDocument.Parse(File.ReadAllText(depsPath));
        var libraries = deps.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(library => library.Name.Split('/')[0]).ToArray();

        Assert.Contains("Microsoft.Maui.DevFlow.Agent.Gtk", libraries);
        Assert.Contains("Microsoft.Maui.DevFlow.Blazor.Gtk", libraries);
        Assert.Contains("Microsoft.Maui.Platforms.Linux.Gtk4", libraries);
        Assert.Contains("Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView", libraries);
        Assert.DoesNotContain(libraries,
            name => name.StartsWith("Platform.Maui.Linux.Gtk", StringComparison.OrdinalIgnoreCase));
    }
}
