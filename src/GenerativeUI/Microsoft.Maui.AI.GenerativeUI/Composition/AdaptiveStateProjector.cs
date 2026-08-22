using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Maui.AI.GenerativeUI.Binding;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>
/// The sole typed-state-to-<see cref="UiObject"/> projection boundary for adaptive surfaces.
/// </summary>
public sealed class AdaptiveStateProjector
{
    public void Project<T>(
        AdaptiveSurfaceSession session,
        string path,
        T value,
        JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        session.ThrowIfDisposed();

        ProjectJson(session, path, JsonSerializer.SerializeToElement(value, jsonTypeInfo));
    }

    public void ProjectJson(AdaptiveSurfaceSession session, string path, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        session.ThrowIfDisposed();

        var target = session.StateRoot;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            target = target[segment];

        UiObjectBuilder.Replace(target, value);
        session.NotifyStateProjected();
    }
}
