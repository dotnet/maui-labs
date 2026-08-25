using Microsoft.Maui.AI.Indexer;
using Microsoft.Maui.AI.Navigation;
using Microsoft.Maui.AI.Wayfinding;

namespace Microsoft.Maui.AI.Navigation.Tests;

public class ApplicationMapServiceTests
{
    [Fact]
    public void Search_PastOrders_MapsToReachableOrdersPage()
    {
        var (service, navigation, _) = CreateService();

        var result = service.Search("Where are my past orders?");

        var match = Assert.Single(result);
        Assert.Equal("OrdersPage", match.Destination.PageName);
        Assert.Equal("//main/orders", match.Destination.Route);
        Assert.Equal(["MainPage", "OrdersPage"], match.Destination.PagePath);
        Assert.Null(navigation.LastNavigatedRoute);
    }

    [Fact]
    public void Search_WhenOnlyOneMeaningfulTermMatches_KeepsRankedResults()
    {
        var (service, _, _) = CreateService();

        var result = service.Search("find orders");

        Assert.Contains(result, match =>
            match.Destination.PageName == "OrdersPage");
    }

    [Fact]
    public void ResolveDestination_SemanticQueryWithMultipleMatches_IsAmbiguous()
    {
        var (service, _, _) = CreateService();

        var result = service.ResolveDestination("review feature");

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
    public async Task NavigateAsync_ReflectedDestination_UsesRegisteredShellRoute()
    {
        var (service, navigation, _) = CreateService();

        var result = await service.NavigateAsync(
            "ProductReviewPage",
            new Dictionary<string, string> { ["sku"] = "seed-basil" });

        Assert.Equal(DestinationResolutionStatus.Success, result.Status);
        Assert.Equal("review?sku=seed-basil", result.Route);
        Assert.Equal(result.Route, navigation.LastNavigatedRoute);
        Assert.Equal("review/<sku>", result.Destination!.RouteTemplate);
        Assert.Equal(
            ["MainPage", "ProductReviewPage"],
            result.Destination.PagePath);
    }

    [Fact]
    public async Task NavigateAsync_RouteQualifiedParameter_UsesRegisteredRouteValue()
    {
        var (service, navigation, _) = CreateService();

        var result = await service.NavigateAsync(
            "ProductReviewPage",
            new Dictionary<string, string>
            {
                ["review.sku"] = "seed-basil",
            });

        Assert.Equal(DestinationResolutionStatus.Success, result.Status);
        Assert.Equal("review?sku=seed-basil", navigation.LastNavigatedRoute);
    }

    [Fact]
    public async Task NavigateAsync_AmbiguousIntent_DoesNotNavigate()
    {
        var (service, navigation, _) = CreateService();

        var result = await service.NavigateAsync("review feature");

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
                "AreaA.SettingsPage"),
        ]);
        var service = new ApplicationMapService(
            catalog,
            navigation,
            new StubCurrentPageContext(
                new CurrentPageSnapshot("SettingsPage", "# Area A")));

        var destination = Assert.Single(service.GetDestinations());

        Assert.Equal("AreaA.SettingsPage", destination.PageTypeName);
        Assert.Equal("# Area A", destination.Markdown);
        Assert.Null(service.GetIndexedPage("SettingsPage"));
        Assert.Equal(
            "# Area A",
            service.GetIndexedPage("AreaA.SettingsPage")?.Markdown);
    }

    [Fact]
    public async Task NavigateAsync_MultiSegmentNativeRoute_PreservesQueryMetadata()
    {
        var catalog = new StubCatalog(
        [
            new IndexedPage(
                "MainPage",
                "MainPage.xaml",
                "- Heading (level 1): \"Home\"",
                "//main/home"),
            new IndexedPage(
                "ProductPage",
                "ProductPage.xaml",
                "- Heading (level 1): \"Product\"",
                [],
                "Example.ProductPage"),
        ]);
        var navigation = new RecordingNavigationService(
        [
            new RouteInfo("home", "//main/home", []),
            new RouteInfo(
                "catalog/product",
                "catalog/product",
                [new QueryParameterInfo("sku", "Sku", "String")],
                "Example.ProductPage"),
        ]);
        var service = new ApplicationMapService(
            catalog,
            navigation,
            new StubCurrentPageContext(
                new CurrentPageSnapshot("MainPage", "# Home")));

        var result = await service.NavigateAsync(
            "ProductPage",
            new Dictionary<string, string> { ["sku"] = "seed-basil" });

        Assert.Equal(DestinationResolutionStatus.Success, result.Status);
        Assert.Equal("catalog/product?sku=seed-basil", result.Route);
        Assert.Equal("catalog/product/<sku>", result.Destination!.RouteTemplate);
        Assert.Equal(result.Route, navigation.LastNavigatedRoute);
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
                "ProductDetailPage"),
            new(
                "review",
                "review",
                [new QueryParameterInfo("sku", "Sku", "String")],
                "ProductReviewPage"),
            new(
                "order",
                "order",
                [new QueryParameterInfo("orderId", "OrderId", "String")],
                "OrderDetailPage"),
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
