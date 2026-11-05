using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// The result of selecting native preference keys or the tracked-key fallback.
/// </summary>
/// <param name="Keys">The ordered, de-duplicated set of preference keys.</param>
/// <param name="Source">
/// <c>"native"</c> when the platform store was enumerated (authoritative), or
/// <c>"registry"</c> when only DevFlow-tracked keys are available (best-effort).
/// </param>
/// <param name="Complete">
/// <c>true</c> when the list reflects the full backing store; <c>false</c> when
/// it only reflects keys DevFlow itself wrote (app-set keys may be missing).
/// </param>
internal readonly record struct PreferenceKeySet(
    IReadOnlyList<string> Keys,
    string Source,
    bool Complete);

/// <summary>
/// Pure (platform-free) helper that selects platform-enumerated keys or the
/// tracked-key fallback and reports how complete the result
/// is. Kept free of any MAUI/platform dependency so it can be unit tested.
/// </summary>
internal static class PreferenceKeyMerger
{
    public const string SourceNative = "native";
    public const string SourceRegistry = "registry";

    /// <summary>
    /// Use authoritative native keys when available, otherwise use the registry.
    /// </summary>
    /// <param name="registryKeys">Keys DevFlow has tracked via set/delete.</param>
    /// <param name="nativeKeys">
    /// Keys enumerated from the platform store, or <c>null</c> when the platform
    /// cannot enumerate its store. When non-null, the result is marked complete.
    /// </param>
    /// <param name="excludeKeys">Internal keys to omit (e.g. DevFlow's registry key).</param>
    public static PreferenceKeySet Merge(
        IEnumerable<string> registryKeys,
        IReadOnlyCollection<string>? nativeKeys,
        IEnumerable<string>? excludeKeys = null)
    {
        var exclude = excludeKeys is null
            ? new HashSet<string>(System.StringComparer.Ordinal)
            : new HashSet<string>(excludeKeys, System.StringComparer.Ordinal);

        var set = new HashSet<string>(System.StringComparer.Ordinal);
        var nativeSupported = nativeKeys is not null;

        var keys = nativeKeys is not null ? nativeKeys : registryKeys;
        foreach (var key in keys)
        {
            if (key is not null)
                set.Add(key);
        }

        set.ExceptWith(exclude);

        var ordered = set.OrderBy(k => k, System.StringComparer.Ordinal).ToList();
        return new PreferenceKeySet(
            ordered,
            nativeSupported ? SourceNative : SourceRegistry,
            nativeSupported);
    }
}
