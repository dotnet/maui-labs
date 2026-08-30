namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Canonical, collision-resistant keys used to bind approved selector scopes.</summary>
public static class MauiTestAgentSelectorScopeKey
{
    public static string? FromSelector(FlowSelector? selector)
    {
        if (selector is null)
            return null;
        if (selector.HasScopedStableItem)
            return ScopedItem(selector.CollectionScope!, selector.StableItemKey!, selector.AutomationId!);
        if (!string.IsNullOrWhiteSpace(selector.AutomationId))
            return "automationId:" + Escape(selector.AutomationId);
        if (selector.TypeIndex is { Type: { Length: > 0 } type, Index: >= 0 } index)
            return $"typeIndex:{Escape(type)}:{index.Index}";
        if (selector.SelectorKind == "typeIndex" &&
            !string.IsNullOrWhiteSpace(selector.Type) &&
            selector.Index is >= 0)
        {
            return $"typeIndex:{Escape(selector.Type)}:{selector.Index}";
        }
        return null;
    }

    public static string ScopedItem(string collectionScope, string stableItemKey, string automationId)
        => $"scopedItem:{Escape(collectionScope)}:{Escape(stableItemKey)}:{Escape(automationId)}";

    private static string Escape(string value) => Uri.EscapeDataString(value.Trim());
}
