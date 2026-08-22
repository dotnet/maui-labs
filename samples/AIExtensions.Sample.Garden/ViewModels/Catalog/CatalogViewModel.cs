using System.Collections.ObjectModel;
using AIExtensions.Sample.Garden.Shared;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Owns the product catalog data and the add-to-cart action.
/// </summary>
public sealed partial class CatalogViewModel : ObservableObject
{
    private readonly GardenDataStore _store;

    public CatalogViewModel(GardenDataStore store)
    {
        _store = store;
    }

    public ObservableCollection<CatalogItemViewModel> Products { get; } = [];

    public ObservableCollection<CatalogGroupViewModel> Groups { get; } = [];

    public ObservableCollection<string> Categories { get; } = ["All"];

    public GardenDataStore Store => _store;

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = "All";

    public IReadOnlyList<Product> FilteredProducts => _store.Products
        .Where(product =>
            (SelectedCategory == "All" ||
             string.Equals(product.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(SearchText) ||
             product.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
             product.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _store.RefreshCatalogAsync(cancellationToken);
            RebuildGroups(_store.Products);
            Categories.Clear();
            Categories.Add("All");
            foreach (var category in _store.Products
                         .Select(product => product.Category)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(category => category))
            {
                Categories.Add(category);
            }
            if (string.IsNullOrWhiteSpace(SelectedCategory) ||
                !Categories.Contains(SelectedCategory))
            {
                SelectedCategory = "All";
            }
            ErrorMessage = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    public async Task AddToCartAsync(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return;

        try
        {
            await _store.AddToCartAsync(sku);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void RebuildGroups(IEnumerable<Product> products)
    {
        Products.Clear();
        Groups.Clear();
        foreach (var grouping in products.GroupBy(p => p.Category).OrderBy(g => g.Key))
        {
            var group = new CatalogGroupViewModel(grouping.Key);
            foreach (var product in grouping.OrderBy(p => p.Name))
            {
                var item = new CatalogItemViewModel(product);
                group.Add(item);
                Products.Add(item);
            }
            Groups.Add(group);
        }
    }
}
