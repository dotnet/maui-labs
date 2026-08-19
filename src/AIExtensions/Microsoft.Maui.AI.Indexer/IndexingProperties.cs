using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// Attached properties that control compile-time and runtime UI indexing.
/// </summary>
public static class IndexingProperties
{
    /// <summary>
    /// Identifies an auxiliary UI subtree that should not be included in AI-facing indexes.
    /// </summary>
    /// <remarks>
    /// This is intended for out-of-band assistant/debug chrome that is already supplying the AI
    /// experience itself. Do not use it to hide ordinary app UI from accessibility tooling.
    /// </remarks>
    public static readonly BindableProperty ExcludeWithChildrenProperty = BindableProperty.CreateAttached(
        "ExcludeWithChildren",
        typeof(bool),
        typeof(IndexingProperties),
        false,
        propertyChanged: OnExcludeWithChildrenChanged);

    public static bool GetExcludeWithChildren(BindableObject bindable)
    {
        ArgumentNullException.ThrowIfNull(bindable);
        return (bool)bindable.GetValue(ExcludeWithChildrenProperty);
    }

    public static void SetExcludeWithChildren(BindableObject bindable, bool value)
    {
        ArgumentNullException.ThrowIfNull(bindable);
        bindable.SetValue(ExcludeWithChildrenProperty, value);
    }

    private static void OnExcludeWithChildrenChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        if (bindable is not MenuItem menuItem)
            return;

        menuItem.ParentChanged -= OnMenuItemParentChanged;
        PropagateMenuItemExclusion(menuItem);

        if (newValue is true)
            menuItem.ParentChanged += OnMenuItemParentChanged;
    }

    private static void OnMenuItemParentChanged(object? sender, EventArgs e)
    {
        if (sender is MenuItem menuItem)
            PropagateMenuItemExclusion(menuItem);
    }

    private static void PropagateMenuItemExclusion(MenuItem menuItem)
    {
        if (menuItem.Parent is not BindableObject generatedShellItem
            || !string.Equals(
                generatedShellItem.GetType().Name,
                "MenuShellItem",
                StringComparison.Ordinal))
        {
            return;
        }

        generatedShellItem.SetValue(
            ExcludeWithChildrenProperty,
            GetExcludeWithChildren(menuItem));
    }
}
