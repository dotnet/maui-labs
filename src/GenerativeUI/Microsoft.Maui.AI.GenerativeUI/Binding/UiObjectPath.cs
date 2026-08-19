namespace Microsoft.Maui.AI.GenerativeUI.Binding;

/// <summary>
/// Resolves the existing dotted binding paths against a <see cref="UiObject"/> tree without
/// auto-vivifying missing members.
/// </summary>
public static class UiObjectPath
{
    public static UiObject? ResolveDotted(UiObject root, string? dottedPath)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (string.IsNullOrWhiteSpace(dottedPath))
            return root;

        var node = root;
        foreach (var segment in dottedPath.Split(
                     '.',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!node.HasMember(segment))
                return null;

            node = node[segment];
        }

        return node;
    }

    public static bool HasData(UiObject node)
        => node.Value is not null || node.Children.Count > 0 || node.Members.Any();
}
