using System.Reflection;

namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>
/// Loads embedded JS/HTML resource files from the assembly.
/// Scripts are stored in Resources/Scripts/ and embedded at build time.
/// </summary>
internal static class ScriptResources
{
    private static readonly Assembly Assembly = typeof(ScriptResources).Assembly;
    private static readonly Dictionary<string, string> Cache = new();

    /// <summary>
    /// Loads an embedded script resource by filename (e.g. "chobitsu-init.js").
    /// </summary>
    internal static string Load(string filename)
    {
        if (Cache.TryGetValue(filename, out var cached))
            return cached;

        var resourceName = $"Microsoft.Maui.DevFlow.Blazor.Resources.Scripts.{filename.Replace('/', '.')}";
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        Cache[filename] = content;
        return content;
    }
}
