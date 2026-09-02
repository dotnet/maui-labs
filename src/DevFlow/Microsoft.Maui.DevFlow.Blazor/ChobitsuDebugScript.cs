namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>
/// Provides the JavaScript code needed to enable Chrome DevTools Protocol debugging
/// in a WebView using Chobitsu.
///
/// Chobitsu is a JavaScript implementation of the Chrome DevTools Protocol that runs
/// entirely in the browser/WebView. CDP commands are sent via EvaluateJavaScriptAsync
/// and responses are returned synchronously in the same JS eval call.
///
/// Architecture:
/// ┌─────────────────────────────────────────────────────────────────┐
/// │  MAUI App                                                       │
/// │  ┌─────────────────────────────────────────────────────────┐   │
/// │  │  BlazorWebView                                           │   │
/// │  │  ┌─────────────────────────────────────────────────────┐ │   │
/// │  │  │  Chobitsu (CDP Implementation in JS)                │ │   │
/// │  │  │  ↕ Single JS Eval (send + receive)                  │ │   │
/// │  │  │  ← HTTP POST /api/cdp ─────────────────────────────┼─┼───┼──← CLI
/// │  │  └─────────────────────────────────────────────────────┘ │   │
/// │  └─────────────────────────────────────────────────────────┘   │
/// └─────────────────────────────────────────────────────────────────┘
/// </summary>
public static class ChobitsuDebugScript
{

    private static string? _cachedChobitsuJs;

    /// <summary>
    /// Loads chobitsu.js from the embedded resource in this assembly.
    /// Used as a fallback for re-injection after Page.reload.
    /// </summary>
    public static string GetEmbeddedChobitsuJs()
    {
        if (_cachedChobitsuJs != null) return _cachedChobitsuJs;

        var assembly = typeof(ChobitsuDebugScript).Assembly;
        using var stream = assembly.GetManifestResourceStream("Microsoft.Maui.DevFlow.Blazor.chobitsu.js")
            ?? throw new InvalidOperationException("Embedded chobitsu.js resource not found in Microsoft.Maui.DevFlow.Blazor assembly.");
        using var reader = new System.IO.StreamReader(stream);
        _cachedChobitsuJs = reader.ReadToEnd();
        return _cachedChobitsuJs;
    }

    /// <summary>
    /// Gets the JavaScript code to inject into the WebView to initialize chobitsu.
    /// Expects chobitsu.js to already be loaded via a script tag in index.html.
    /// </summary>
    public static string GetInjectionScript()
    {
        return ScriptResources.Load("chobitsu-init.js");
    }

    public static string GetLoadScript()
    {
        return ScriptResources.Load("load-chobitsu.js");
    }
}
