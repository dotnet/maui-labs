using AIExtensions.Sample.Garden.Services;
using AIExtensions.Sample.Garden.Shared;
using AIExtensions.Sample.Garden.ViewModels;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Pages;

public partial class ProductDetailPage : AdaptiveContentPage, IQueryAttributable
{
    private readonly ProductDetailViewModel _viewModel;
    private readonly AdaptiveStateProjector _projector;
    private readonly AdaptiveComponentCatalogBuilder _catalogBuilder;

    public ProductDetailPage(
        ProductDetailViewModel vm,
        IAdaptiveSurfaceSessionFactory sessionFactory,
        AdaptiveSurfaceCoordinator coordinator,
        AdaptiveStateProjector projector,
        AdaptiveComponentCatalogBuilder catalogBuilder)
        : base(
            sessionFactory,
            coordinator,
            GardenAdaptiveLayouts.ProductSurface,
            GardenAdaptiveLayouts.ProductStandard)
    {
        InitializeComponent();
        BindingContext = _viewModel = vm;
        _projector = projector;
        _catalogBuilder = catalogBuilder;
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

        _projector.Project(
            Session,
            "product",
            _viewModel.CurrentProduct,
            GardenJsonContext.Default.Product);
        return true;
    }

    protected override ValueTask<AdaptiveSurfaceContext> CreateAdaptiveContextAsync(
        PresentationIntentContext presentation,
        CancellationToken cancellationToken)
    {
        var product = _viewModel.CurrentProduct
            ?? throw new InvalidOperationException("The product must be loaded before composing its surface.");
        var dataManifest = new AdaptiveDataDescriptor[]
        {
            new()
            {
                Path = "product",
                Contract = nameof(Product),
                Description = "The selected server-backed Garden product and its available facets.",
            },
        };
        var regions = new[]
        {
            new AdaptiveRegionDescriptor
            {
                Name = GardenAdaptiveLayouts.ProductBodyRegion,
                Description = "The adaptive product information body above fixed purchase and review actions.",
            },
        };
        var display = DeviceDisplay.Current.MainDisplayInfo;
        var context = new AdaptiveSurfaceContext
        {
            SurfaceInstanceId = Session.SurfaceInstanceId,
            Surface = new()
            {
                Surface = GardenAdaptiveLayouts.ProductSurface,
                Description =
                    "Arrange product information for the user's current question. Purchase and review actions remain fixed.",
                Regions = regions,
            },
            DataManifest = dataManifest,
            ComponentCatalog = _catalogBuilder.Build(
                Session.StateRoot,
                dataManifest,
                [GardenAdaptiveLayouts.ProductBodyRegion]),
            Viewport = new()
            {
                Width = Width > 0 ? Width : display.Width / display.Density,
                Height = Height > 0 ? Height : display.Height / display.Density,
                Density = display.Density,
                Idiom = DeviceInfo.Current.Idiom.ToString(),
                Orientation = display.Orientation.ToString(),
            },
            Intent = presentation.Intent,
            RecentContext = presentation.RecentUserContext,
            StateSignature = string.Join(
                ':',
                product.Sku,
                product.Dimensions is not null,
                product.ColorOptions is not null,
                product.SeedDetails is not null,
                _viewModel.HasReviews),
        };
        return ValueTask.FromResult(context);
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
