using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Indexer;
using Microsoft.Maui.AI.Navigation;
using Microsoft.Maui.AI.Wayfinding;

namespace Microsoft.Maui.AI.Navigation.Tests;

public sealed class MauiWayfindingMiddlewareTests
{
    [Fact]
    public async Task UseMauiWayfinding_AddsCuratedToolsAndEphemeralPolicy()
    {
        var original = new[]
        {
            new ChatMessage(ChatRole.System, "App instructions"),
            new ChatMessage(ChatRole.User, "Where are orders?"),
        };
        var services = CreateServices(enableVision: false);
        using var provider = services.BuildServiceProvider();
        var inner = new RecordingChatClient();
        var client = new ChatClientBuilder(inner)
            .UseMauiWayfinding()
            .Build(provider);

        await client.GetResponseAsync(original);

        Assert.Equal(2, original.Length);
        Assert.Contains(
            inner.Messages!,
            message => message.Role == ChatRole.System
                && message.Text!.Contains("MAUI WAYFINDING"));
        Assert.Contains(
            inner.Messages!,
            message => message.Role == ChatRole.System
                && message.Text!.Contains("Navigate only when the"));
        Assert.Equal(
            [
                "get_app_destination",
                "get_current_app_state",
                "navigate_to_app_destination",
                "search_app_ui",
            ],
            inner.Options!.Tools!
                .Select(tool => tool.Name)
                .OrderBy(name => name)
                .ToArray());
    }

    [Fact]
    public async Task UseMauiWayfinding_VisionEnabled_AddsVisualTool()
    {
        var services = CreateServices(enableVision: true);
        services.AddSingleton<IChatClient>(new RecordingChatClient());
        using var provider = services.BuildServiceProvider();
        var inner = new RecordingChatClient();
        var client = new ChatClientBuilder(inner)
            .UseMauiWayfinding()
            .Build(provider);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What does this chart show?")]);

        Assert.Contains(
            inner.Options!.Tools!,
            tool => tool.Name == "describe_current_visual");
    }

    [Fact]
    public async Task Tools_ReturnUserFacingStateWithoutRoutesOrClrNames()
    {
        var services = CreateServices(enableVision: false);
        using var provider = services.BuildServiceProvider();
        var tools = provider.GetRequiredService<MauiWayfindingTools>();

        var current = await tools.GetCurrentAppStateAsync(includePageUi: true);
        var search = Assert.Single(tools.SearchAppUi("past orders"));
        var destinationResult = tools.GetAppDestination(search.DestinationId);
        var destination = destinationResult.Destination;

        Assert.Equal("Orders", current.CurrentPage);
        Assert.DoesNotContain("//", current.ToString());
        Assert.DoesNotContain("OrdersPage", current.ToString());
        Assert.DoesNotContain("OrderInsightsView", current.ToString());
        Assert.DoesNotContain("- SalesChart:", current.ToString());
        Assert.DoesNotContain("- SecretChart:", current.ToString());
        Assert.Equal("orders", search.DestinationId);
        Assert.DoesNotContain("//", search.ToString());
        Assert.NotNull(destination);
        Assert.True(destinationResult.Found);
        Assert.DoesNotContain("//", destination.ToString());
        Assert.DoesNotContain("Pages/", destination.ToString());
        Assert.DoesNotContain("ClearCommand", destination.Pages[0].UiDescription);
        Assert.DoesNotContain("{Orders}", destination.Pages[0].UiDescription);
        Assert.DoesNotContain("IsNormalMode", destination.Pages[0].UiDescription);
        Assert.DoesNotContain("CartTotal", destination.Pages[0].UiDescription);
        Assert.DoesNotContain("OpenCommand", destination.Pages[0].UiDescription);
        Assert.Contains("[shown in some states]", destination.Pages[0].UiDescription);
        Assert.Contains(
            "automationId: \"OrderInsightsCharts\"",
            current.PageUi);
        Assert.Contains("- Control: \"Order insights charts\"", current.PageUi);
        Assert.Contains("- Control: \"Quarterly sales chart\"", current.PageUi);
        Assert.Contains("- Control: \"Undescribed type chart\"", current.PageUi);
        Assert.Contains(
            "automationId: \"OrderInsightsCharts\"",
            destination.Pages[0].UiDescription);
    }

    [Fact]
    public async Task GetCurrentAppState_AppShell_UsesConfiguredNavigationTitle()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentPageContextProvider>(
            new StubCurrentPageContext(
                new CurrentPageSnapshot("AppShell", "# Current flyout")));
        services.AddSingleton<ShellNavigationService>(
            new StubNavigationService([]));
        services.AddMauiWayfinding(
            new TestCatalog([]),
            options => options.NavigationMenuTitle = "App menu");
        using var provider = services.BuildServiceProvider();
        var tools = provider.GetRequiredService<MauiWayfindingTools>();

        var current = await tools.GetCurrentAppStateAsync();

        Assert.Equal("App menu", current.CurrentPage);
        Assert.DoesNotContain("AppShell", current.ToString());
    }

    [Fact]
    public void GetAppDestination_ReflectedRoute_IncludesHomeAndTargetUi()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentPageContextProvider>(
            new StubCurrentPageContext(
                new CurrentPageSnapshot("MainPage", "", "Sage")));
        services.AddSingleton<ShellNavigationService>(
            new StubNavigationService(
            [
                new RouteInfo("chat", "//main/chat", []),
                new RouteInfo("products", "//main/products", []),
                new RouteInfo(
                    "product",
                    "product",
                    [new QueryParameterInfo("sku", "Sku", "String")],
                    "ProductDetailPage"),
                new RouteInfo(
                    "review",
                    "review",
                    [new QueryParameterInfo("sku", "Sku", "String")],
                    "ProductReviewPage"),
            ]));
        services.AddMauiWayfinding(
            new TestCatalog(
            [
                new IndexedPage(
                    "MainPage",
                    "Pages/MainPage.xaml",
                    "- Heading (level 1): \"Sage\"",
                    ["//main/chat"],
                    "MainPage",
                    "Sage",
                    [Element(IndexedElementKind.Heading, "Sage")]),
                new IndexedPage(
                    "CatalogPage",
                    "Pages/CatalogPage.xaml",
                    "- Heading (level 1): \"Products\"\n- Button: \"Details\"",
                    ["//main/products"],
                    "CatalogPage",
                    "Products",
                    [
                        Element(IndexedElementKind.Heading, "Products"),
                        Element(IndexedElementKind.Action, "Details"),
                    ]),
                new IndexedPage(
                    "ProductDetailPage",
                    "Pages/ProductDetailPage.xaml",
                    "- Heading (level 1): \"{Name}\"\n- Button: \"Write Review\"",
                    [],
                    "ProductDetailPage",
                    null,
                    [Element(IndexedElementKind.Action, "Write Review")]),
                new IndexedPage(
                    "ProductReviewPage",
                    "Pages/ProductReviewPage.xaml",
                    "- Heading (level 1): \"Write Review\"\n- Button: \"Submit Review\"",
                    [],
                    "ProductReviewPage",
                    "Write Review",
                    [
                        Element(IndexedElementKind.Heading, "Write Review"),
                        Element(IndexedElementKind.Action, "Submit Review"),
                    ]),
            ],
            "MainPage"));
        using var provider = services.BuildServiceProvider();
        var tools = provider.GetRequiredService<MauiWayfindingTools>();

        var destinationResult = tools.GetAppDestination("write-review");
        var destination = destinationResult.Destination;

        Assert.NotNull(destination);
        Assert.True(destinationResult.Found);
        Assert.Equal(
            ["Sage", "Write Review"],
            destination.Path);
        Assert.Equal(2, destination.Pages.Count);
        Assert.Contains(
            destination.Pages,
            page => page.Title == "Write Review"
                && page.UiDescription.Contains("Submit Review"));
    }

    private static ServiceCollection CreateServices(bool enableVision)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentPageContextProvider>(
            new StubCurrentPageContext(
                new CurrentPageSnapshot(
                    "OrdersPage",
                    """
                    # Current UI: OrdersPage
                    - Current page: OrdersPage
                    - Heading (level 1): "Orders"
                    - OrderInsightsView: "Order insights charts" [automationId: "OrderInsightsCharts"]
                    - SalesChart: "Quarterly sales chart" [automationId: "QuarterlySalesChart"]
                    - SecretChart: "Undescribed type chart"
                    """,
                    "Past Orders",
                    ["OrderInsightsCharts", "QuarterlySalesChart"],
                    [
                        Element(IndexedElementKind.Heading, "Orders"),
                        Element(IndexedElementKind.Unknown, "Order insights charts", automationId: "OrderInsightsCharts"),
                        Element(IndexedElementKind.Unknown, "Quarterly sales chart", automationId: "QuarterlySalesChart"),
                        Element(IndexedElementKind.Unknown, "Undescribed type chart"),
                    ])));
        services.AddSingleton<ShellNavigationService>(
            new StubNavigationService(
            [
                new RouteInfo("orders", "//main/orders", []),
            ]));
        services.AddMauiWayfinding(
            new TestCatalog(
            [
                new IndexedPage(
                    "OrdersPage",
                    "Pages/OrdersPage.xaml",
                    """
                    # OrdersPage

                    File: Pages/OrdersPage.xaml

                    - Heading (level 1): "Orders"
                    - Button: "Clear All" → ClearCommand
                    - CollectionView: "{Orders}" [visible when IsNormalMode = true]
                    - Button: "Total: {CartTotal:C}" → BindingContext.OpenCommand
                    - OrderInsightsView: "Order insights charts" [automationId: "OrderInsightsCharts"]
                    """,
                    ["//main/orders"],
                    "OrdersPage",
                    "Orders",
                    [
                        Element(IndexedElementKind.Heading, "Orders"),
                        Element(IndexedElementKind.Action, "Clear All"),
                        Element(IndexedElementKind.Collection, null, isConditional: true),
                        Element(IndexedElementKind.Unknown, "Order insights charts", automationId: "OrderInsightsCharts"),
                    ]),
            ]),
            options =>
            {
                options.EnableVision = enableVision;
            });
        return services;
    }

    private static IndexedElement Element(
        IndexedElementKind kind,
        string? text,
        string? automationId = null,
        bool isConditional = false)
        => new(
            kind,
            text,
            null,
            automationId,
            null,
            false,
            kind == IndexedElementKind.Action,
            isConditional,
            0,
            []);

    private sealed class TestCatalog(
        IReadOnlyList<IndexedPage> pages,
        string? entryPageName = null)
        : IndexedPageCatalog
    {
        public override IReadOnlyList<IndexedPage> Pages => pages;
        public override string? EntryPageName => entryPageName;
    }

    private sealed class StubNavigationService(IReadOnlyList<RouteInfo> routes)
        : ShellNavigationService
    {
        public override IReadOnlyList<RouteInfo> GetRoutes() => routes;
    }

    private sealed class StubCurrentPageContext(CurrentPageSnapshot snapshot)
        : ICurrentPageContextProvider
    {
        public Task<CurrentPageSnapshot?> CaptureAsync(
            CurrentPageSnapshotOptions? options = null)
            => Task.FromResult<CurrentPageSnapshot?>(snapshot);
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage>? Messages { get; private set; }
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            Options = options;
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            Options = options;
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }
}
