namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Group header for catalog categories.
/// </summary>
public sealed class CatalogGroupViewModel(string categoryName) : List<CatalogItemViewModel>
{
    public string CategoryName { get; } = categoryName;
}
