namespace Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

/// <summary>
/// Supplies the <see cref="XamlSourceMap"/> for a XAML-defined element type (a page/view whose
/// root is declared in a <c>.xaml</c> file). Implemented by the build-time map generator; the
/// runtime <see cref="VisualTreeWalker"/> queries it (when set) to attach source locations.
/// </summary>
public interface IXamlSourceMapProvider
{
    /// <summary>
    /// Returns the source map whose root is the type named <paramref name="fullTypeName"/> (the
    /// XAML file's root type's full CLR name, e.g. <c>"MyApp.MainPage"</c>), or null if that type
    /// has no static XAML source.
    /// </summary>
    XamlSourceMap? GetMap(string fullTypeName);
}
