using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace DevFlow.Sample;

internal static class PreferenceDiagnostics
{
    public static void Register(AgentExtension diagnostics)
    {
        // Direct app writes/removals must not update DevFlow's tracked-key registry.
        diagnostics.MapTool(
            "seed_pref",
            "Writes or removes a preference directly via the app's Preferences store, bypassing DevFlow tracking.",
            "POST",
            "seed-pref",
            request =>
            {
                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body);
                var root = document.RootElement;
                var key = root.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
                if (key is null)
                    return Task.FromResult(HttpResponse.Error("'key' is required."));

                var value = root.TryGetProperty("value", out var valueElement) ? valueElement.GetString() : null;
                var sharedName = root.TryGetProperty("sharedName", out var sharedElement) ? sharedElement.GetString() : null;
                var remove = root.TryGetProperty("remove", out var removeElement) && removeElement.GetBoolean();

                if (remove)
                    Microsoft.Maui.Storage.Preferences.Default.Remove(key, sharedName);
                else
                    Microsoft.Maui.Storage.Preferences.Default.Set(key, value ?? string.Empty, sharedName);

                return Task.FromResult(HttpResponse.Json(new Dictionary<string, object?>
                {
                    ["key"] = key,
                    ["value"] = value ?? string.Empty,
                    ["sharedName"] = sharedName,
                    ["seeded"] = !remove
                }));
            },
            parameters: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "key": { "type": "string" },
                "value": { "type": "string" },
                "sharedName": { "type": "string" },
                "remove": { "type": "boolean" }
              },
              "required": ["key"]
            }
            """).RootElement.Clone(),
            annotations: new ExtensionToolAnnotations { Category = "diagnostics" });
    }
}
