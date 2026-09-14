namespace Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

/// <summary>
/// Supplies the <see cref="XamlSourceMap"/> for a XAML-defined element type.
/// Implemented by the build-time source-map generator.
/// </summary>
public interface IXamlSourceMapProvider
{
    XamlSourceMap? GetMap(string fullTypeName);
}
