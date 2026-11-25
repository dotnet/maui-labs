namespace Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

/// <summary>
/// Supplies the <see cref="XamlSourceMap"/> for a XAML-defined element type.
/// Implemented by the build-time source-map generator.
/// </summary>
public interface IXamlSourceMapProvider
{
    XamlSourceMap? GetMap(string fullTypeName);

    /// <summary>
    /// Discards data derived from code that a Hot Reload metadata update may have changed.
    /// </summary>
    void InvalidateCache()
    {
    }
}
