using AIExtensions.Sample.Garden.Components;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using AIExtensions.Sample.Garden.Shared;
using AIExtensions.Sample.Garden.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Pages;

public partial class MainPage :
    AdaptiveContentPage,
    IRecipient<CartChangedMessage>,
    IRecipient<OrdersChangedMessage>
{
    private readonly MainViewModel _viewModel;
    private readonly AdaptiveStateProjector _projector;
    private readonly GardenAdaptiveContextFactory _contextFactory;

    public MainPage(
        MainViewModel viewModel,
        IAdaptiveSurfaceSessionFactory sessionFactory,
        AdaptiveSurfaceCoordinator coordinator,
        AdaptiveStateProjector projector,
        GardenAdaptiveContextFactory contextFactory)
        : base(
            sessionFactory,
            coordinator,
            GardenAdaptiveLayouts.HomeSurface,
            GardenAdaptiveLayouts.HomeStandard)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _projector = projector;
        _contextFactory = contextFactory;
        AttachAdaptiveRegion(AdaptiveBody);
        WeakReferenceMessenger.Default.Register<CartChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<OrdersChangedMessage>(this);
    }

    protected override async Task<bool> PrepareAdaptiveStateAsync(
        CancellationToken cancellationToken = default)
    {
        await _viewModel.InitializeAsync(cancellationToken);
        ProjectState();
        return true;
    }

    protected override ValueTask<AdaptiveSurfaceContext> CreateAdaptiveContextAsync(
        PresentationIntentContext presentation,
        CancellationToken cancellationToken)
    {
        ProjectState();
        var store = _viewModel.Store;
        var manifest = DataManifest(store.Recommendation is not null);
        return ValueTask.FromResult(_contextFactory.Create(
            Session,
            GardenAdaptiveLayouts.Surface(
                GardenAdaptiveLayouts.HomeSurface,
                GardenAdaptiveLayouts.HomeBodyRegion,
                "Shape the Garden home around the user's durable goal while preserving Sage chat and global navigation."),
            manifest,
            presentation,
            $"products:{store.Products.Count}:cart:{store.CartItems.Count}:orders:{store.Orders.Count}:recommendation:{store.Recommendation is not null}",
            Width,
            Height));
    }

    private void ProjectState()
    {
        var store = _viewModel.Store;
        _projector.Project(Session, "catalog", store.Products.ToList(), GardenJsonContext.Default.ListProduct);
        _projector.Project(
            Session,
            "cart",
            new Cart(store.CartItems.ToArray(), store.CartTotal),
            GardenJsonContext.Default.Cart);
        _projector.Project(Session, "orders", store.Orders.ToList(), GardenJsonContext.Default.ListOrder);
        if (store.Recommendation is not null)
        {
            _projector.Project(
                Session,
                "recommendation",
                store.Recommendation,
                GardenJsonContext.Default.Recommendation);
        }
    }

    private static IReadOnlyList<AdaptiveDataDescriptor> DataManifest(bool hasRecommendation)
        =>
        [
            Data("catalog", GardenDataContracts.ProductList, "The complete current product catalog."),
            Data("cart", GardenDataContracts.Cart, "The current server-backed cart."),
            Data("orders", GardenDataContracts.OrderList, "Recent server-backed orders."),
            Data(
                "recommendation",
                GardenDataContracts.Recommendation,
                "The current curated starter recommendation.",
                hasRecommendation,
                "No recommendation is currently available."),
        ];

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

    private async void OnProductsClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//main/products");

    private async void OnOrdersClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//main/orders");

    void IRecipient<CartChangedMessage>.Receive(CartChangedMessage message)
        => _ = RefreshAdaptiveSurfaceAsync();

    void IRecipient<OrdersChangedMessage>.Receive(OrdersChangedMessage message)
        => _ = RefreshAdaptiveSurfaceAsync();
}
