using Microsoft.Maui.AI.Indexer;
using Microsoft.Maui.AI.Navigation;

namespace Microsoft.Maui.AI.Navigation.Tests;

public class ApplicationMapServiceTests
{
    [Fact]
    public void Search_PastOrders_MapsToReachableOrdersPage()
    {
        var (service, navigation, _) = CreateService();

        var result = service.Search("Where are my past orders?");

        Assert.NotEmpty(result);
        var match = result[0];
        Assert.Equal("OrdersPage", match.Destination.PageName);
        Assert.Equal("//main/orders", match.Destination.Route);
        Assert.Equal(["MainPage", "OrdersPage"], match.Destination.PagePath);
        Assert.Null(navigation.LastNavigatedRoute);
    }

    [Fact]
    public void ResolveDestination_SemanticQueryWithMultipleMatches_IsAmbiguous()
    {
        var (service, _, _) = CreateService();

        var result = service.ResolveDestination("review");

        Assert.Equal(DestinationResolutionStatus.Ambiguous, result.Status);
        Assert.Contains(result.Candidates, candidate => candidate.PageName == "ProductDetailPage");
        Assert.Contains(result.Candidates, candidate => candidate.PageName == "ProductReviewPage");
    }

    [Fact]
    public void ResolveDestination_ParameterizedPageWithoutValue_ReportsMissingParameter()
    {
        var (service, _, _) = CreateService();

        var result = service.ResolveDestination("ProductReviewPage");

        Assert.Equal(DestinationResolutionStatus.MissingParameters, result.Status);
        Assert.Equal(["sku"], result.MissingParameters);
        Assert.Null(result.Route);
    }

    [Fact]
    public async Task CaptureCurrentPageAsync_ReturnsLiveCurrentPageContext()
    {
        var (service, _, context) = CreateService();

        var result = await service.CaptureCurrentPageAsync();

        Assert.Same(context.Snapshot, result);
        Assert.Equal("ProductReviewPage", result!.PageName);
        Assert.Contains("Editor", result.Markdown);
    }

    [Fact]
    public async Task NavigateAsync_ResolvedDestination_UsesSingleDeepRoute()
    {
        var (service, navigation, _) = CreateService();

        var result = await service.NavigateAsync(
            "ProductReviewPage",
            new Dictionary<string, string> { ["sku"] = "seed-basil" });

        Assert.Equal(DestinationResolutionStatus.Success, result.Status);
        Assert.Equal(
            "//main/products/product/review?sku=seed-basil&product.sku=seed-basil",
            result.Route);
        Assert.Equal(result.Route, navigation.LastNavigatedRoute);
        Assert.Equal(
            "//main/products/product/<sku>/review",
            result.Destination!.RouteTemplate);
        Assert.Equal(
            ["MainPage", "CatalogPage", "ProductDetailPage", "ProductReviewPage"],
            result.Destination.PagePath);
    }

    [Fact]
    public async Task NavigateAsync_RouteQualifiedParameters_CanUseDistinctValues()
    {
        var (service, navigation, _) = CreateService();

        var result = await service.NavigateAsync(
            "ProductReviewPage",
            new Dictionary<string, string>
            {
                ["product.sku"] = "seed-tomato",
                ["review.sku"] = "seed-basil",
            });

        Assert.Equal(DestinationResolutionStatus.Success, result.Status);
        Assert.Equal(
            "//main/products/product/review?sku=seed-basil&product.sku=seed-tomato",
            navigation.LastNavigatedRoute);
    }

    [Fact]
    public async Task NavigateAsync_AmbiguousIntent_DoesNotNavigate()
    {
        var (service, navigation, _) = CreateService();

        var result = await service.NavigateAsync("review");

        Assert.Equal(DestinationResolutionStatus.Ambiguous, result.Status);
        Assert.Null(navigation.LastNavigatedRoute);
    }

    [Fact]
    public void GetDestinations_PageWithMultipleRoutes_UsesUniqueDestinationIds()
    {
        var catalog = new StubCatalog(
        [
            new IndexedPage(
                "MainPage",
                "MainPage.xaml",
                "# MainPage",
                ["//main/chat", "//main/help"],
                "Example.MainPage"),
        ]);
        var navigation = new RecordingNavigationService(
        [
            new RouteInfo("chat", "//main/chat", []),
            new RouteInfo("help", "//main/help", []),
        ]);
        var service = new ApplicationMapService(
            catalog,
            navigation,
            new StubCurrentPageContext(
                new CurrentPageSnapshot("MainPage", "# MainPage")));

        var destinations = service.GetDestinations();

        Assert.Equal(2, destinations.Count);
        Assert.Contains(destinations, destination =>
            destination.DestinationId == "Example.MainPage@//main/chat");
        Assert.Contains(destinations, destination =>
            destination.DestinationId == "Example.MainPage@//main/help");
    }

    [Fact]
    public void GetDestinations_QualifiedTarget_DoesNotCrossMatchSameNamedPage()
    {
        var catalog = new StubCatalog(
        [
            new IndexedPage(
                "SettingsPage",
                "AreaA/SettingsPage.xaml",
                "# Area A",
                [],
                "AreaA.SettingsPage"),
            new IndexedPage(
                "SettingsPage",
                "AreaB/SettingsPage.xaml",
                "# Area B",
                [],
                "AreaB.SettingsPage"),
        ]);
        var navigation = new RecordingNavigationService(
        [
            new RouteInfo(
                "settings",
                "settings",
                [],
                "AreaA.SettingsPage",
                "//main"),
        ]);
        var service = new ApplicationMapService(
            catalog,
            navigation,
            new StubCurrentPageContext(
                new CurrentPageSnapshot("SettingsPage", "# Area A")));

        var destination = Assert.Single(service.GetDestinations());

        Assert.Equal("AreaA.SettingsPage", destination.PageTypeName);
        Assert.Equal("# Area A", destination.Markdown);
    }

    private static (
        ApplicationMapService Service,
        RecordingNavigationService Navigation,
        StubCurrentPageContext Context) CreateService()
    {
        var routes = new RouteInfo[]
        {
            new("chat", "//main/chat", []),
            new("products", "//main/products", []),
            new("orders", "//main/orders", []),
            new(
                "product",
                "product",
                [new QueryParameterInfo("sku", "Sku", "String")],
                "ProductDetailPage",
                "//main/products"),
            new(
                "review",
                "review",
                [new QueryParameterInfo("sku", "Sku", "String")],
                "ProductReviewPage",
                "//main/products/product"),
            new(
                "order",
                "order",
                [new QueryParameterInfo("orderId", "OrderId", "String")],
                "OrderDetailPage",
                "//main/orders"),
            new("cart", "cart", [], "CartPage"),
        };

        var catalog = new StubCatalog(
        [
            new("MainPage", "Pages/MainPage.xaml", "# MainPage\n- Button: \"Products\"\n- Button: \"Orders\"", "//main/chat"),
            new("CatalogPage", "Pages/CatalogPage.xaml", "# CatalogPage\n- Heading: \"Products\"\n- Button: \"Details\"", "//main/products"),
            new("OrdersPage", "Pages/OrdersPage.xaml", "# OrdersPage\n- Heading: \"Past Orders\"", "//main/orders"),
            new("ProductDetailPage", "Pages/ProductDetailPage.xaml", "# ProductDetailPage\n- Button: \"Write Review\""),
            new("ProductReviewPage", "Pages/ProductReviewPage.xaml", "# ProductReviewPage\n- Heading: \"Write Review\"\n- Button: \"Submit Review\""),
            new("OrderDetailPage", "Pages/OrderDetailPage.xaml", "# OrderDetailPage\n- Heading: \"Order Details\""),
            new("CartPage", "Pages/CartPage.xaml", "# CartPage\n- Heading: \"Shopping Cart\""),
        ]);

        var navigation = new RecordingNavigationService(routes);
        var context = new StubCurrentPageContext(
            new CurrentPageSnapshot(
                "ProductReviewPage",
                "# ProductReviewPage\n- Editor: [placeholder: \"Share your experience...\"]"));

        return (
            new ApplicationMapService(catalog, navigation, context),
            navigation,
            context);
    }

    private sealed class StubCatalog(IReadOnlyList<IndexedPage> pages)
        : IndexedPageCatalog
    {
        public override IReadOnlyList<IndexedPage> Pages => pages;

        public override string? EntryPageName => "MainPage";
    }

    private sealed class RecordingNavigationService(IReadOnlyList<RouteInfo> routes)
        : ShellNavigationService
    {
        public string? LastNavigatedRoute { get; private set; }

        public override IReadOnlyList<RouteInfo> GetRoutes() => routes;

        public override Task<string> NavigateAsync(string route)
        {
            LastNavigatedRoute = route;
            return Task.FromResult(route);
        }
    }

    private sealed class StubCurrentPageContext(CurrentPageSnapshot snapshot)
        : ICurrentPageContextProvider
    {
        public CurrentPageSnapshot Snapshot => snapshot;

        public Task<CurrentPageSnapshot?> CaptureAsync(
            CurrentPageSnapshotOptions? options = null)
            => Task.FromResult<CurrentPageSnapshot?>(snapshot);
    }
}
