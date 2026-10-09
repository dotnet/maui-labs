namespace Microsoft.Maui.DevFlow.WebView;

/// <summary>Embedded, host-neutral CDP scripts. No HTTP/static-asset host is required.</summary>
public static class ChobitsuDebugScript
{
    public static string GetEmbeddedChobitsuJs() => ScriptResources.Load("chobitsu.js");
    public static string GetInjectionScript() => ScriptResources.Load("chobitsu-init.js");
    public static string GetLoadScript()
        => "if (typeof chobitsu === 'undefined') {\n" + GetEmbeddedChobitsuJs() + "\n}";
}
