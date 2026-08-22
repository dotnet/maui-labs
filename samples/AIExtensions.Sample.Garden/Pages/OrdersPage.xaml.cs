using AIExtensions.Sample.Garden.Components;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using AIExtensions.Sample.Garden.Shared;
using AIExtensions.Sample.Garden.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Pages;

public partial class OrdersPage : AdaptiveContentPage, IRecipient<OrdersChangedMessage>
{
    private readonly OrdersViewModel _viewModel;
    private readonly AdaptiveStateProjector _projector;
    private readonly GardenAdaptiveContextFactory _contextFactory;

    public OrdersPage(
        OrdersViewModel viewModel,
        IAdaptiveSurfaceSessionFactory sessionFactory,
        AdaptiveSurfaceCoordinator coordinator,
        AdaptiveStateProjector projector,
        GardenAdaptiveContextFactory contextFactory)
        : base(
            sessionFactory,
            coordinator,
            GardenAdaptiveLayouts.OrdersSurface,
            GardenAdaptiveLayouts.OrdersStandard)
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
        ProjectState();
        UpdateStandardLayout();
        return true;
    }

    protected override ValueTask<AdaptiveSurfaceContext> CreateAdaptiveContextAsync(
        PresentationIntentContext presentation,
        CancellationToken cancellationToken)
    {
        ProjectState();
        var hasOrders = _viewModel.Store.Orders.Count > 0;
        UpdateStandardLayout();
        var manifest = new AdaptiveDataDescriptor[]
        {
            new()
            {
                Path = "orders",
                Contract = GardenDataContracts.OrderList,
                Description = "Complete server-backed order history including purchased line items and totals.",
                Available = hasOrders,
                UnavailableReason = hasOrders ? null : "Order history is empty.",
            },
            new()
            {
                Path = "orders",
                Contract = GardenDataContracts.EmptyOrderList,
                Description = "No-orders state.",
                Available = !hasOrders,
                UnavailableReason = !hasOrders
                    ? null
                    : "Order history currently contains orders.",
            },
        };
        return ValueTask.FromResult(_contextFactory.Create(
            Session,
            GardenAdaptiveLayouts.Surface(
                GardenAdaptiveLayouts.OrdersSurface,
                GardenAdaptiveLayouts.OrdersBodyRegion,
                "Emphasize matching purchases, chronology, or spending while preserving app-authored Open and Reorder actions.",
                GardenAdaptiveLayouts.Require(
                    "order access and reorder actions",
                    hasOrders
                        ?
                        [
                            GardenComponentCatalog.OrdersListAlias,
                            GardenComponentCatalog.OrderTimelineAlias,
                            GardenComponentCatalog.OrderSummaryAlias,
                            GardenComponentCatalog.OrderDetailAlias,
                        ]
                        : [GardenComponentCatalog.OrdersEmptyStateAlias])),
            manifest,
            presentation,
            $"orders:{string.Join(',', _viewModel.Store.Orders.Select(order => order.Id))}",
            Width,
            Height));
    }

    private void ProjectState()
        => _projector.Project(
            Session,
            "orders",
            _viewModel.Store.Orders.ToList(),
            GardenJsonContext.Default.ListOrder);

    private void UpdateStandardLayout()
        => Session.SetStandardLayout(
            _viewModel.Store.Orders.Count > 0
                ? GardenAdaptiveLayouts.OrdersStandard
                : GardenAdaptiveLayouts.OrdersEmptyStandard);

    private async void OnBackClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//main/chat");

    void IRecipient<OrdersChangedMessage>.Receive(OrdersChangedMessage message)
        => _ = RefreshAdaptiveSurfaceAsync();
}
