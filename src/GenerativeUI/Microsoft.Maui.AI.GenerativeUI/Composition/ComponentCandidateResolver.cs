using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Registry;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

public sealed record ResolvedComponentCandidate(ComponentDescriptor Descriptor, string DataPath);

/// <summary>
/// Filters the registered catalog to components compatible with the active data contract and state.
/// </summary>
public sealed class ComponentCandidateResolver(GenerativeUiRegistry registry)
{
    public IReadOnlyList<ResolvedComponentCandidate> Resolve(
        UiObject stateRoot,
        string dataContract,
        string dataPath)
    {
        ArgumentNullException.ThrowIfNull(stateRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataContract);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataPath);

        var data = UiObjectPath.ResolveDotted(stateRoot, dataPath);
        if (data is null)
            return [];

        return
        [
            .. registry.Components
                .Select(registration => registration.Descriptor)
                .Where(descriptor =>
                    string.Equals(descriptor.DataContract, dataContract, StringComparison.OrdinalIgnoreCase) &&
                    descriptor.RequiredBindings.All(binding =>
                        UiObjectPath.ResolveDotted(data, binding) is { } node && UiObjectPath.HasData(node)))
                .OrderBy(descriptor => descriptor.Alias, StringComparer.OrdinalIgnoreCase)
                .Select(descriptor => new ResolvedComponentCandidate(descriptor, dataPath)),
        ];
    }
}
