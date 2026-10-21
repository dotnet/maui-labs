using System.Reflection;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.WebView;

/// <summary>
/// Loads embedded JS/HTML resource files from the assembly.
/// Scripts are stored in Resources/Scripts/ and embedded at build time.
/// </summary>
internal static class ScriptResources
{
    private static readonly Assembly Assembly = typeof(ScriptResources).Assembly;
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    internal static string BuildCdpSendScript(string escapedCommand, string? nativeBinding)
        => Load("cdp-send-receive.js")
            .Replace("%NATIVE_CDP_BINDING%", JsonSerializer.Serialize(nativeBinding))
            .Replace("%CDP_MESSAGE%", escapedCommand);

    internal static string BuildInsertTextScript(string escapedText, int textLength)
        => Load("insert-text.js")
            .Replace("%TEXT_LENGTH%", textLength.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("%TEXT%", escapedText);

    /// <summary>
    /// Loads an embedded script resource by filename (e.g. "chobitsu-init.js").
    /// </summary>
    internal static string Load(string filename)
    {
        if (Cache.TryGetValue(filename, out var cached))
            return cached;

        var resourceName = $"Microsoft.Maui.DevFlow.WebView.Resources.Scripts.{filename.Replace('/', '.')}";
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        Cache[filename] = content;
        return content;
    }
}
