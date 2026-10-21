using System.Text.Json;

namespace Microsoft.Maui.DevFlow.WebView;

internal sealed class DocumentReloadFence
{
    private readonly string _property = JsonSerializer.Serialize("__devflow_reload_" + Guid.NewGuid().ToString("N"));

    internal string MarkCurrentDocumentScript =>
        $"(function(){{Object.defineProperty(document,{_property},{{value:true}});return 'marked';}})()";

    internal string ReadinessScript =>
        $"(function(){{return Object.prototype.hasOwnProperty.call(document,{_property}) ? 'reloading' : document.readyState;}})()";
}
