using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Registry;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>
/// Builds a complete catalog that tells the model why every registered component is or is not usable.
/// </summary>
public sealed class AdaptiveComponentCatalogBuilder(GenerativeUiRegistry registry)
{
    public IReadOnlyList<AdaptiveComponentCatalogEntry> Build(
        UiObject stateRoot,
        IReadOnlyList<AdaptiveDataDescriptor> dataManifest,
        IReadOnlyList<string> surfaceRegions)
    {
        ArgumentNullException.ThrowIfNull(stateRoot);
        ArgumentNullException.ThrowIfNull(dataManifest);
        ArgumentNullException.ThrowIfNull(surfaceRegions);

        var entries = new List<AdaptiveComponentCatalogEntry>();
        foreach (var registration in registry.Components.OrderBy(
                     item => item.Descriptor.Alias,
                     StringComparer.Ordinal))
        {
            var descriptor = registration.Descriptor;
            var compatiblePaths = dataManifest
                .Where(data => data.Available)
                .Where(data => string.Equals(data.Contract, descriptor.DataContract, StringComparison.OrdinalIgnoreCase))
                .Where(data =>
                {
                    var node = UiObjectPath.ResolveDotted(stateRoot, data.Path);
                    return node is not null &&
                           descriptor.RequiredBindings.All(binding =>
                               UiObjectPath.ResolveDotted(node, binding) is { } required &&
                               UiObjectPath.HasData(required));
                })
                .Select(data => data.Path)
                .ToArray();

            var allowedRegions = descriptor.AllowedRegions.Count == 0
                ? surfaceRegions.ToArray()
                : descriptor.AllowedRegions
                    .Where(region => surfaceRegions.Contains(region, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
            var available = compatiblePaths.Length > 0 && allowedRegions.Length > 0;

            entries.Add(new AdaptiveComponentCatalogEntry
            {
                Alias = descriptor.Alias,
                Description = descriptor.Description,
                WhenNotToUse = descriptor.WhenNotToUse,
                DataContract = descriptor.DataContract,
                RequiredBindings = descriptor.RequiredBindings,
                OptionalBindings = descriptor.OptionalBindings,
                Variants = descriptor.Variants,
                AllowedRegions = allowedRegions,
                CompatibleDataPaths = compatiblePaths,
                Available = available,
                UnavailableReason = available
                    ? null
                    : DescribeUnavailable(descriptor, dataManifest, compatiblePaths, allowedRegions),
            });
        }

        return entries;
    }

    private static string DescribeUnavailable(
        ComponentDescriptor descriptor,
        IReadOnlyList<AdaptiveDataDescriptor> dataManifest,
        IReadOnlyList<string> compatiblePaths,
        IReadOnlyList<string> allowedRegions)
    {
        if (allowedRegions.Count == 0)
            return "The component is not allowed in any region on this surface.";

        if (!dataManifest.Any(data =>
                data.Available &&
                string.Equals(data.Contract, descriptor.DataContract, StringComparison.OrdinalIgnoreCase)))
        {
            return $"No available data path exposes the '{descriptor.DataContract}' contract.";
        }

        if (compatiblePaths.Count == 0)
            return "Available data is missing one or more required bindings.";

        return "The component is unavailable.";
    }
}
