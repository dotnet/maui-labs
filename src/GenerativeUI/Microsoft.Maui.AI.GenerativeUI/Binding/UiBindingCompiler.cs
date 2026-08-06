using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.GenerativeUI.Binding;

/// <summary>
/// Compiles DSL <c>bind</c>/<c>key</c> paths into MAUI indexer bindings against a
/// <see cref="UiObject"/> root: a dotted path <c>a.b.c</c> becomes <c>[a][b][c].Value</c>.
/// </summary>
public static class UiBindingCompiler
{
    /// <summary>Converts a dotted DSL path into a MAUI indexer binding path ending in <c>.Value</c>.</summary>
    public static string ToBindingPath(string dottedPath)
    {
        if (string.IsNullOrWhiteSpace(dottedPath))
            return "Value";

        var segments = dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return "Value";

        // [a][b][c].Value
        return string.Concat(segments.Select(s => $"[{s}]")) + ".Value";
    }

    /// <summary>
    /// Creates a MAUI <see cref="Microsoft.Maui.Controls.Binding"/> for a DSL path against a
    /// <see cref="UiObject"/> root. One-way for display <c>bind</c>, two-way for editable <c>key</c>.
    /// </summary>
    public static Microsoft.Maui.Controls.Binding Compile(
        string dottedPath,
        BindingMode mode = BindingMode.OneWay,
        IValueConverter? converter = null,
        object? source = null)
        => new(ToBindingPath(dottedPath), mode)
        {
            Converter = converter,
            Source = source,
        };
}
