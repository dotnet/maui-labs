using AIExtensions.Sample.Garden.Components;
using AIExtensions.Sample.Garden.Services;
using AIExtensions.Sample.Garden.Shared;
using AIExtensions.Sample.Garden.ViewModels;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Pages;

public partial class ProductDetailPage : AdaptiveContentPage, IQueryAttributable
{
    private readonly ProductDetailViewModel _viewModel;
    private readonly AdaptiveStateProjector _projector;
    private readonly GardenAdaptiveContextFactory _contextFactory;

    public ProductDetailPage(
        ProductDetailViewModel vm,
        IAdaptiveSurfaceSessionFactory sessionFactory,
        AdaptiveSurfaceCoordinator coordinator,
        AdaptiveStateProjector projector,
        GardenAdaptiveContextFactory contextFactory)
        : base(
            sessionFactory,
            coordinator,
            GardenAdaptiveLayouts.ProductSurface,
            GardenAdaptiveLayouts.ProductStandard)
    {
        InitializeComponent();
        BindingContext = _viewModel = vm;
        _projector = projector;
        _contextFactory = contextFactory;
        AttachAdaptiveRegion(AdaptiveBody);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("sku", out var sku) && sku is string s)
        {
            if (BindingContext is ProductDetailViewModel vm)
                vm.Sku = s;
        }
    }

    protected override async Task<bool> PrepareAdaptiveStateAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Sku))
            return false;

        await _viewModel.LoadAsync(_viewModel.Sku, cancellationToken);
        if (_viewModel.CurrentProduct is null)
            return false;

        if (_viewModel.Store.Products.Count == 0)
            await _viewModel.Store.RefreshCatalogAsync(cancellationToken);
        ProjectState();
        return true;
    }

    protected override ValueTask<AdaptiveSurfaceContext> CreateAdaptiveContextAsync(
        PresentationIntentContext presentation,
        CancellationToken cancellationToken)
    {
        var product = _viewModel.CurrentProduct
            ?? throw new InvalidOperationException("The product must be loaded before composing its surface.");
        ProjectState();
        var dataManifest = new AdaptiveDataDescriptor[]
        {
            new()
            {
                Path = "product",
                Contract = nameof(Product),
                Description = "The selected server-backed Garden product and its available facets.",
            },
            new()
            {
                Path = "reviews",
                Contract = GardenDataContracts.ReviewList,
                Description = "Customer reviews for the selected product.",
            },
            new()
            {
                Path = "related",
                Contract = GardenDataContracts.ProductList,
                Description = "Other products in the selected product's category.",
            },
        };
        var context = _contextFactory.Create(
            Session,
            GardenAdaptiveLayouts.Surface(
                GardenAdaptiveLayouts.ProductSurface,
                GardenAdaptiveLayouts.ProductBodyRegion,
                "Arrange product information for the user's question. Add to Cart and Write Review remain fixed.",
                GardenAdaptiveLayouts.Require(
                    "essential product information",
                    GardenComponentCatalog.ProductCoreInfoAlias)),
            dataManifest,
            presentation,
            string.Join(
                ':',
                product.Sku,
                product.Dimensions is not null,
                product.ColorOptions is not null,
                product.SeedDetails is not null,
                _viewModel.CurrentReviews.Count),
            Width,
            Height);
        return ValueTask.FromResult(context);
    }

    private void ProjectState()
    {
        var product = _viewModel.CurrentProduct
            ?? throw new InvalidOperationException("The product must be loaded before projecting its state.");
        _projector.Project(Session, "product", product, GardenJsonContext.Default.Product);
        _projector.Project(
            Session,
            "reviews",
            _viewModel.CurrentReviews.ToList(),
            GardenJsonContext.Default.ListReview);
        _projector.Project(
            Session,
            "related",
            _viewModel.Store.Products
                .Where(candidate =>
                    candidate.Sku != product.Sku &&
                    string.Equals(candidate.Category, product.Category, StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToList(),
            GardenJsonContext.Default.ListProduct);
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
