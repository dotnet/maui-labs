namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>Source-compatible facade for the shared embedded scripts.</summary>
public static class ChobitsuDebugScript
{
    public static string GetEmbeddedChobitsuJs() => WebView.ChobitsuDebugScript.GetEmbeddedChobitsuJs();
    public static string GetInjectionScript() => WebView.ChobitsuDebugScript.GetInjectionScript();
    public static string GetLoadScript() => WebView.ChobitsuDebugScript.GetLoadScript();
}
