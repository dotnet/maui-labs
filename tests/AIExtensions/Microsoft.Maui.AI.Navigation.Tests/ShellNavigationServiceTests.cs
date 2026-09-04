using Microsoft.Maui.AI.Navigation;

namespace Microsoft.Maui.AI.Navigation.Tests;

public class QueryParameterDiscoveryTests
{
    [QueryProperty(nameof(Sku), "sku")]
    private class FakePageWithQueryProperty
    {
        public string? Sku { get; set; }
    }

    [QueryProperty(nameof(OrderId), "orderId")]
    [QueryProperty(nameof(Status), "status")]
    private class FakePageWithMultipleQueryProperties
    {
        public string? OrderId { get; set; }
        public string? Status { get; set; }
    }

    private class FakePageWithoutQueryProperty
    {
    }

    [Fact]
    public void DiscoverQueryParameters_FindsSingleQueryProperty()
    {
        var result = ShellNavigationService.DiscoverQueryParameters(typeof(FakePageWithQueryProperty));

        Assert.Single(result);
        Assert.Equal("sku", result[0].QueryName);
        Assert.Equal("Sku", result[0].PropertyName);
        Assert.Equal("String", result[0].PropertyType);
    }

    [Fact]
    public void DiscoverQueryParameters_FindsMultipleQueryProperties()
    {
        var result = ShellNavigationService.DiscoverQueryParameters(typeof(FakePageWithMultipleQueryProperties));

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.QueryName == "orderId");
        Assert.Contains(result, r => r.QueryName == "status");
    }

    [Fact]
    public void DiscoverQueryParameters_ReturnsEmptyForNoAttributes()
    {
        var result = ShellNavigationService.DiscoverQueryParameters(typeof(FakePageWithoutQueryProperty));
        Assert.Empty(result);
    }

    [Fact]
    public void DiscoverQueryParameters_ReturnsEmptyForNull()
    {
        var result = ShellNavigationService.DiscoverQueryParameters(null);
        Assert.Empty(result);
    }

    [QueryProperty(nameof(Id), "id")]
    private class FakeVM
    {
        public string? Id { get; set; }
    }

    private class FakePageWithVMCtor
    {
        public FakePageWithVMCtor(FakeVM vm) { }
    }

    [Fact]
    public void DiscoverQueryParameters_FindsParametersOnVMFromConstructor()
    {
        var result = ShellNavigationService.DiscoverQueryParameters(typeof(FakePageWithVMCtor));

        Assert.Single(result);
        Assert.Equal("id", result[0].QueryName);
        Assert.Equal("Id", result[0].PropertyName);
    }

    [QueryProperty(nameof(Sku), "sku")]
    private class FakePageWithDuplicateParam
    {
        public string? Sku { get; set; }
        public FakePageWithDuplicateParam(FakeVMWithSameSku vm) { }
    }

    [QueryProperty(nameof(Sku), "sku")]
    private class FakeVMWithSameSku
    {
        public string? Sku { get; set; }
    }

    [Fact]
    public void DiscoverQueryParameters_DeduplicatesAcrossPageAndVM()
    {
        var result = ShellNavigationService.DiscoverQueryParameters(typeof(FakePageWithDuplicateParam));

        Assert.Single(result);
        Assert.Equal("sku", result[0].QueryName);
    }
}

public class RouteInfoTests
{
    [Fact]
    public void RouteInfo_RecordEquality()
    {
        var a = new RouteInfo("products", "//main/products", []);
        var b = new RouteInfo("products", "//main/products", []);
        Assert.Equal(a.Route, b.Route);
        Assert.Equal(a.FullPath, b.FullPath);
    }

    [Fact]
    public void QueryParameterInfo_RecordEquality()
    {
        var a = new QueryParameterInfo("sku", "Sku", "String");
        var b = new QueryParameterInfo("sku", "Sku", "String");
        Assert.Equal(a, b);
    }
}

public class BuildRouteTests
{
    private class TestableNavigationService : ShellNavigationService
    {
        private readonly IReadOnlyList<RouteInfo> _routes;
        public TestableNavigationService(IReadOnlyList<RouteInfo> routes) => _routes = routes;
        public override IReadOnlyList<RouteInfo> GetRoutes() => _routes;
    }

    private static TestableNavigationService CreateService() => new(
    [
        new RouteInfo("products", "//main/products", []),
        new RouteInfo("product", "product",
            [new QueryParameterInfo("sku", "Sku", "String")]),
        new RouteInfo("review", "review",
            [new QueryParameterInfo("sku", "Sku", "String")]),
        new RouteInfo("orders", "//main/orders", []),
        new RouteInfo("order", "order",
            [new QueryParameterInfo("orderId", "OrderId", "String")]),
    ]);

    [Fact]
    public void BuildRoute_NoParameters_JoinsSegments()
    {
        var svc = CreateService();
        var route = svc.BuildRoute("//main/products", ["product", "review"]);
        Assert.Equal("//main/products/product/review", route);
    }

    [Fact]
    public void BuildRoute_SharedParameter_AppliedToAllMatchingSegments()
    {
        var svc = CreateService();
        var route = svc.BuildRoute("//main/products",
            ["product", "review"],
            new Dictionary<string, string> { ["sku"] = "seed-tomato" });

        Assert.Equal("//main/products/product/review?sku=seed-tomato&product.sku=seed-tomato", route);
    }

    [Fact]
    public void BuildRoute_SharedParameter_ProducesValidUri()
    {
        var svc = CreateService();
        var route = svc.BuildRoute("//main/products",
            ["product", "review"],
            new Dictionary<string, string> { ["sku"] = "seed-tomato" });

        var qIndex = route.IndexOf('?');
        if (qIndex >= 0)
            Assert.DoesNotContain("?", route[(qIndex + 1)..]);
    }

    [Fact]
    public void BuildRoute_SingleSegment_NoPrefix()
    {
        var svc = CreateService();
        var route = svc.BuildRoute("//main/products",
            ["product"],
            new Dictionary<string, string> { ["sku"] = "seed-tomato" });

        Assert.Equal("//main/products/product?sku=seed-tomato", route);
        Assert.DoesNotContain("product.sku", route);
    }

    [Fact]
    public void BuildRoute_SingleSegmentWithParam_QueryOnThatSegment()
    {
        var svc = CreateService();
        var route = svc.BuildRoute("//main/orders",
            ["order"],
            new Dictionary<string, string> { ["orderId"] = "ORD-00001" });

        Assert.Equal("//main/orders/order?orderId=ORD-00001", route);
    }

    [Fact]
    public void BuildRoute_UnknownParameter_NotAttached()
    {
        var svc = CreateService();
        var route = svc.BuildRoute("//main/products",
            ["product"],
            new Dictionary<string, string> { ["unknown"] = "value" });

        Assert.Equal("//main/products/product", route);
    }

    [Fact]
    public void BuildRoute_EmptySegments_ReturnsBasePath()
    {
        var svc = CreateService();
        var route = svc.BuildRoute("//main/products", []);
        Assert.Equal("//main/products", route);
    }

    [Fact]
    public void BuildRoute_EscapesSpecialCharacters()
    {
        var svc = CreateService();
        var route = svc.BuildRoute("//main/products",
            ["product"],
            new Dictionary<string, string> { ["sku"] = "seed tomato&fresh" });

        Assert.Contains("seed%20tomato%26fresh", route);
    }

    [Fact]
    public void BuildRoute_RelativeBase_DoesNotAddLeadingSlash()
    {
        var svc = CreateService();
        var route = svc.BuildRoute("",
            ["product"],
            new Dictionary<string, string> { ["sku"] = "seed-tomato" });

        Assert.Equal("product?sku=seed-tomato", route);
    }
}

public class ResolveRouteTests
{
    private class TestableNavigationService : ShellNavigationService
    {
        private readonly IReadOnlyList<RouteInfo> _routes;
        public TestableNavigationService(IReadOnlyList<RouteInfo> routes) => _routes = routes;
        public override IReadOnlyList<RouteInfo> GetRoutes() => _routes;
    }

    private static TestableNavigationService CreateService() => new(
    [
        new RouteInfo("chat", "//main/chat", []),
        new RouteInfo("products", "//main/products", []),
        new RouteInfo("orders", "//main/orders", []),
        new RouteInfo("product", "product",
            [new QueryParameterInfo("sku", "Sku", "String")]),
        new RouteInfo("review", "review",
            [new QueryParameterInfo("sku", "Sku", "String")]),
        new RouteInfo("order", "order",
            [new QueryParameterInfo("orderId", "OrderId", "String")]),
        new RouteInfo("cart", "cart", []),
    ]);

    [Theory]
    [InlineData("//main/products")]
    [InlineData("//main/orders")]
    [InlineData("//main/chat")]
    public void Resolve_HierarchyOnly_PassesThrough(string uri)
    {
        Assert.Equal(uri, CreateService().ResolveRoute(uri));
    }

    [Fact]
    public void Resolve_ProductWithSku_ConvertsInlineValueToQuery()
    {
        var route = CreateService().ResolveRoute("//main/products/product/seed-tomato");

        Assert.Equal("//main/products/product?sku=seed-tomato", route);
    }

    [Fact]
    public void Resolve_OrderWithId_ConvertsInlineValueToQuery()
    {
        var route = CreateService().ResolveRoute("//main/orders/order/ORD-00001");

        Assert.Equal("//main/orders/order?orderId=ORD-00001", route);
    }

    [Fact]
    public void Resolve_UrlEncodedValue_PreservesEncoding()
    {
        var route = CreateService().ResolveRoute("//main/products/product/seed%20tomato");

        Assert.Equal("//main/products/product?sku=seed%20tomato", route);
    }

    [Fact]
    public void Resolve_ProductWithoutValue_DoesNotAddQuery()
    {
        var route = CreateService().ResolveRoute("//main/products/product");

        Assert.Equal("//main/products/product", route);
    }

    [Fact]
    public void Resolve_CartWithoutParameters_PreservesPath()
    {
        var route = CreateService().ResolveRoute("//main/products/cart");

        Assert.Equal("//main/products/cart", route);
    }

    [Fact]
    public void Resolve_NestedRoute_UsesSingleUriWithIntermediatePrefix()
    {
        var route = CreateService().ResolveRoute(
            "//main/products/product/seed-tomato/review");

        Assert.Equal(
            "//main/products/product/review?sku=seed-tomato&product.sku=seed-tomato",
            route);
    }

    [Fact]
    public void Resolve_NestedRoute_PreservesDistinctValuesForSameParameterName()
    {
        var route = CreateService().ResolveRoute(
            "//main/products/product/seed-tomato/review/seed-basil");

        Assert.Equal(
            "//main/products/product/review?sku=seed-basil&product.sku=seed-tomato",
            route);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("cart")]
    [InlineData("review")]
    public void Resolve_RelativeNavigationWithoutInlineValues_PassesThrough(string uri)
    {
        Assert.Equal(uri, CreateService().ResolveRoute(uri));
    }

    [Fact]
    public void Resolve_RelativeRouteWithInlineValue_RemainsRelative()
    {
        var route = CreateService().ResolveRoute("product/seed-tomato");

        Assert.Equal("product?sku=seed-tomato", route);
    }

    [Fact]
    public void Resolve_ExplicitQueryString_PassesThrough()
    {
        const string uri =
            "//main/products/product?sku=seed-tomato&highlight=true";

        Assert.Equal(uri, CreateService().ResolveRoute(uri));
    }

    [Theory]
    [InlineData("")]
    [InlineData("//main")]
    public void Resolve_TrivialRoute_PassesThrough(string uri)
    {
        Assert.Equal(uri, CreateService().ResolveRoute(uri));
    }

    [Fact]
    public void Resolve_UnknownTrailingSegment_PassesThrough()
    {
        const string uri = "//main/products/cart/unknown";

        Assert.Equal(uri, CreateService().ResolveRoute(uri));
    }

    [Fact]
    public void Resolve_OutputHasAtMostOneQuestionMark()
    {
        var testCases = new[]
        {
            "//main/products/product/seed-tomato",
            "//main/products/product/seed-tomato/review",
            "//main/orders/order/ORD-00001",
            "//main/products",
            "cart",
            "..",
            "product/seed-tomato",
        };

        var svc = CreateService();
        foreach (var uri in testCases)
        {
            var route = svc.ResolveRoute(uri);
            var questionMarkCount = route.Count(c => c == '?');

            Assert.True(questionMarkCount <= 1,
                $"Resolved route '{route}' (from '{uri}') has {questionMarkCount} '?' characters");
        }
    }

    public class MauiShellParameterPassingTests
    {
        private const string ProductRoute = "ai-navigation-product";
        private const string ReviewRoute = "ai-navigation-review";

        [QueryProperty(nameof(Sku), "sku")]
        private sealed class ProductPage : ContentPage
        {
            public string? Sku { get; set; }
        }

        [QueryProperty(nameof(Sku), "sku")]
        private sealed class ReviewPage : ContentPage
        {
            public string? Sku { get; set; }
        }

        [Fact]
        public async Task GoToAsync_SingleRoute_DeliversParameterToIntermediateAndLastPages()
        {
            Routing.RegisterRoute(ProductRoute, typeof(ProductPage));
            Routing.RegisterRoute(ReviewRoute, typeof(ReviewPage));

            try
            {
                var shell = new Shell();
                shell.Items.Add(new ShellContent
                {
                    Route = "home",
                    Content = new ContentPage()
                });

                await shell.GoToAsync(
                    $"{ProductRoute}/{ReviewRoute}" +
                    $"?{ProductRoute}.sku=seed-tomato&sku=seed-tomato");

                var product = Assert.Single(
                    shell.Navigation.NavigationStack.OfType<ProductPage>());
                var review = Assert.Single(
                    shell.Navigation.NavigationStack.OfType<ReviewPage>());

                Assert.Equal("seed-tomato", product.Sku);
                Assert.Equal("seed-tomato", review.Sku);
            }
            finally
            {
                Routing.UnRegisterRoute(ReviewRoute);
                Routing.UnRegisterRoute(ProductRoute);
            }
        }

        [Fact]
        public async Task GoToAsync_SingleRoute_ScopesDistinctValuesWithSameParameterName()
        {
            Routing.RegisterRoute(ProductRoute, typeof(ProductPage));
            Routing.RegisterRoute(ReviewRoute, typeof(ReviewPage));

            try
            {
                var shell = new Shell();
                shell.Items.Add(new ShellContent
                {
                    Route = "home",
                    Content = new ContentPage()
                });

                await shell.GoToAsync(
                    $"{ProductRoute}/{ReviewRoute}" +
                    $"?{ProductRoute}.sku=seed-tomato&sku=seed-basil");

                var product = Assert.Single(
                    shell.Navigation.NavigationStack.OfType<ProductPage>());
                var review = Assert.Single(
                    shell.Navigation.NavigationStack.OfType<ReviewPage>());

                Assert.Equal("seed-tomato", product.Sku);
                Assert.Equal("seed-basil", review.Sku);
            }
            finally
            {
                Routing.UnRegisterRoute(ReviewRoute);
                Routing.UnRegisterRoute(ProductRoute);
            }
        }
    }
}
