namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>Implemented by repeated-item models that expose a stable app-owned test identity.</summary>
public interface IDevFlowStableItemKey
{
    string? DevFlowStableItemKey { get; }
}

/// <summary>
/// App-supplied test identity for controls realized inside a repeated or virtualized item.
/// Bind this attached property on the item-template root to a stable model identifier.
/// </summary>
public static class DevFlowTest
{
    public static readonly BindableProperty StableItemKeyProperty = BindableProperty.CreateAttached(
        "StableItemKey",
        typeof(string),
        typeof(DevFlowTest),
        default(string));

    public static string? GetStableItemKey(BindableObject target)
        => target.GetValue(StableItemKeyProperty) as string;

    public static void SetStableItemKey(BindableObject target, string? value)
        => target.SetValue(StableItemKeyProperty, value);
}
