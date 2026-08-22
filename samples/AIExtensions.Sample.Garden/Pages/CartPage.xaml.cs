using AIExtensions.Sample.Garden.Components;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using AIExtensions.Sample.Garden.Shared;
using AIExtensions.Sample.Garden.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Pages;

public partial class CartPage : AdaptiveContentPage, IRecipient<CartChangedMessage>
{
    private readonly CartViewModel _viewModel;
    private readonly AdaptiveStateProjector _projector;
    private readonly GardenAdaptiveContextFactory _contextFactory;

    public CartPage(
        CartViewModel viewModel,
        IAdaptiveSurfaceSessionFactory sessionFactory,
        AdaptiveSurfaceCoordinator coordinator,
        AdaptiveStateProjector projector,
        GardenAdaptiveContextFactory contextFactory)
        : base(
            sessionFactory,
            coordinator,
            GardenAdaptiveLayouts.CartSurface,
            GardenAdaptiveLayouts.CartStandard)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _projector = projector;
        _contextFactory = contextFactory;
        AttachAdaptiveRegion(AdaptiveBody);
        WeakReferenceMessenger.Default.Register(this);
    }

    protected override async Task<bool> PrepareAdaptiveStateAsync(
        CancellationToken cancellationToken = default)
    {
        await _viewModel.InitializeAsync(cancellationToken);
        if (_viewModel.Store.Products.Count == 0)
            await _viewModel.Store.RefreshCatalogAsync(cancellationToken);
        ProjectState();
        UpdateStandardLayout();
        return true;
    }

    protected override ValueTask<AdaptiveSurfaceContext> CreateAdaptiveContextAsync(
        PresentationIntentContext presentation,
        CancellationToken cancellationToken)
    {
        ProjectState();
        var hasItems = _viewModel.Store.CartItems.Count > 0;
        UpdateStandardLayout();
        var cartSkus = _viewModel.Store.CartItems.Select(item => item.Sku).ToHashSet(StringComparer.Ordinal);
        var suggestions = _viewModel.Store.Products.Where(product => !cartSkus.Contains(product.Sku)).Take(4).ToList();
        var hasSuggestions = suggestions.Count > 0;
        var manifest = new AdaptiveDataDescriptor[]
        {
            Data(
                "cart",
                GardenDataContracts.Cart,
                "The canonical server-backed cart with totals.",
                hasItems,
                "The cart is empty."),
            Data(
                "cart",
                GardenDataContracts.EmptyCart,
                "Empty-cart state.",
                !hasItems,
                "The cart currently contains items."),
            Data(
                "suggestions",
                GardenDataContracts.ProductList,
                "Products not currently in the cart.",
                hasSuggestions,
                "Every catalog product is already in the cart."),
        };
        return ValueTask.FromResult(_contextFactory.Create(
            Session,
            GardenAdaptiveLayouts.Surface(
                GardenAdaptiveLayouts.CartSurface,
                GardenAdaptiveLayouts.CartBodyRegion,
                "Arrange cart items, budget context, totals, and optional add-ons. Quantity/remove controls and Checkout must remain usable.",
                GardenAdaptiveLayouts.Require(
                    "cart item controls",
                    hasItems
                        ?
                        [
                            GardenComponentCatalog.CartItemsAlias,
                            GardenComponentCatalog.CompactCartItemsAlias,
                        ]
                        : [GardenComponentCatalog.CartEmptyStateAlias])),
            manifest,
            presentation,
            $"items:{string.Join(',', _viewModel.Store.CartItems.Select(item => $"{item.Sku}:{item.Quantity}"))}:total:{_viewModel.Store.CartTotal}",
            Width,
            Height));
    }

    private void ProjectState()
    {
        var store = _viewModel.Store;
        _projector.Project(
            Session,
            "cart",
            new Cart(store.CartItems.ToArray(), store.CartTotal),
            GardenJsonContext.Default.Cart);
        var cartSkus = store.CartItems.Select(item => item.Sku).ToHashSet(StringComparer.Ordinal);
        _projector.Project(
            Session,
            "suggestions",
            store.Products.Where(product => !cartSkus.Contains(product.Sku)).Take(4).ToList(),
            GardenJsonContext.Default.ListProduct);
    }

    private void UpdateStandardLayout()
        => Session.SetStandardLayout(
            _viewModel.Store.CartItems.Count > 0
                ? GardenAdaptiveLayouts.CartStandard
                : GardenAdaptiveLayouts.CartEmptyStandard);

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

    private async void OnCloseClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    void IRecipient<CartChangedMessage>.Receive(CartChangedMessage message)
        => _ = RefreshAdaptiveSurfaceAsync();
}
