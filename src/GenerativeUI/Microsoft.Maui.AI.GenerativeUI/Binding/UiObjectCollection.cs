using System.Collections.ObjectModel;

namespace Microsoft.Maui.AI.GenerativeUI.Binding;

/// <summary>
/// An observable list of <see cref="UiObject"/> nodes, bound as a <c>CollectionView.ItemsSource</c>.
/// Adds/removes raise collection-changed so the UI updates without re-inflation.
/// </summary>
public sealed class UiObjectCollection : ObservableCollection<UiObject>
{
    /// <summary>Returns the first child whose <see cref="UiObject.Name"/> matches, or <c>null</c>.</summary>
    public UiObject? Get(string key)
    {
        foreach (var item in this)
        {
            if (string.Equals(item.Name, key, StringComparison.Ordinal))
                return item;
        }
        return null;
    }
}
