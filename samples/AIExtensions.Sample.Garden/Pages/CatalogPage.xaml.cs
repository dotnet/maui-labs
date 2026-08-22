using AIExtensions.Sample.Garden.Components;
using AIExtensions.Sample.Garden.Services;
using AIExtensions.Sample.Garden.Shared;
using AIExtensions.Sample.Garden.ViewModels;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Pages;

public partial class CatalogPage : AdaptiveContentPage
{
    private readonly CatalogViewModel _viewModel;
    private readonly AdaptiveStateProjector _projector;
    private readonly GardenAdaptiveContextFactory _contextFactory;

    public CatalogPage(
        CatalogViewModel viewModel,
        IAdaptiveSurfaceSessionFactory sessionFactory,
        AdaptiveSurfaceCoordinator coordinator,
        AdaptiveStateProjector projector,
        GardenAdaptiveContextFactory contextFactory)
        : base(
            sessionFactory,
            coordinator,
            GardenAdaptiveLayouts.CatalogSurface,
            GardenAdaptiveLayouts.CatalogStandard)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _projector = projector;
        _contextFactory = contextFactory;
        AttachAdaptiveRegion(AdaptiveBody);
    }

    protected override async Task<bool> PrepareAdaptiveStateAsync(
        CancellationToken cancellationToken = default)
    {
        await _viewModel.InitializeAsync(cancellationToken);
        ProjectState();
        UpdateStandardLayout();
        return true;
    }

    protected override ValueTask<AdaptiveSurfaceContext> CreateAdaptiveContextAsync(
        PresentationIntentContext presentation,
        CancellationToken cancellationToken)
    {
        ProjectState();
        var products = _viewModel.FilteredProducts;
        var recommendation = _viewModel.Store.Recommendation;
        var hasProducts = products.Count > 0;
        UpdateStandardLayout();
        var manifest = new AdaptiveDataDescriptor[]
        {
            Data(
                "catalog",
                GardenDataContracts.ProductList,
                "Products matching the fixed search and category controls.",
                hasProducts,
                "No products match the fixed search and category controls."),
            Data(
                "catalog",
                GardenDataContracts.EmptyProductList,
                "No-results state for the fixed search and category controls.",
                !hasProducts,
                "The catalog currently contains matching products."),
            Data(
                "recommendation",
                GardenDataContracts.Recommendation,
                "Curated products for a current gardening goal.",
                hasProducts && recommendation is not null,
                "No curated recommendation is currently available."),
        };
        return ValueTask.FromResult(_contextFactory.Create(
            Session,
            GardenAdaptiveLayouts.Surface(
                GardenAdaptiveLayouts.CatalogSurface,
                GardenAdaptiveLayouts.CatalogBodyRegion,
                "Choose a browse, list, comparison, or recommendation composition. Keep product navigation and Add actions available.",
                GardenAdaptiveLayouts.Require(
                    "catalog navigation and purchase actions",
                    hasProducts
                        ?
                        [
                            GardenComponentCatalog.CatalogGridAlias,
                            GardenComponentCatalog.CatalogListAlias,
                            GardenComponentCatalog.CategoryShelvesAlias,
                            GardenComponentCatalog.RecommendationStripAlias,
                            GardenComponentCatalog.ComparisonTrayAlias,
                        ]
                        : [GardenComponentCatalog.CatalogEmptyStateAlias])),
            manifest,
            presentation,
            $"products:{string.Join(',', products.Select(product => product.Sku))}:search:{_viewModel.SearchText}:category:{_viewModel.SelectedCategory}",
            Width,
            Height));
    }

    private void ProjectState()
    {
        _projector.Project(
            Session,
            "catalog",
            _viewModel.FilteredProducts.ToList(),
            GardenJsonContext.Default.ListProduct);
        if (_viewModel.Store.Recommendation is not null)
        {
            _projector.Project(
                Session,
                "recommendation",
                _viewModel.Store.Recommendation,
                GardenJsonContext.Default.Recommendation);
        }
    }

    private void UpdateStandardLayout()
        => Session.SetStandardLayout(
            _viewModel.FilteredProducts.Count > 0
                ? GardenAdaptiveLayouts.CatalogStandard
                : GardenAdaptiveLayouts.CatalogEmptyStandard);

    private static AdaptiveDataDescriptor Data(
        string path,
        string contract,
        string description,
        bool available = true,
        string? unavailableReason = null)
        => new()
        {
            Path = path,
            Contract = contract,
            Description = description,
            Available = available,
            UnavailableReason = unavailableReason,
        };

    private async void OnFilterChanged(object? sender, EventArgs e)
        => await RefreshAdaptiveSurfaceAsync();

    private async void OnCartClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("cart");

    private async void OnBackClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//main/chat");
}
