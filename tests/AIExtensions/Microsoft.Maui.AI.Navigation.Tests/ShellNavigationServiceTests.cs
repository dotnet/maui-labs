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
}

public class ParseRouteTests
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

    // ── Hierarchy-only routes ───────────────────────────────────────

    [Fact]
    public void Parse_HierarchyOnly_SingleStep()
    {
        var steps = CreateService().ParseRoute("//main/products");
        Assert.Single(steps);
        Assert.Equal("//main/products", steps[0].route);
    }

    [Fact]
    public void Parse_HierarchyOnly_DifferentTab()
    {
        var steps = CreateService().ParseRoute("//main/orders");
        Assert.Single(steps);
        Assert.Equal("//main/orders", steps[0].route);
    }

    [Fact]
    public void Parse_HierarchyOnly_ChatTab()
    {
        var steps = CreateService().ParseRoute("//main/chat");
        Assert.Single(steps);
        Assert.Equal("//main/chat", steps[0].route);
    }

    // ── Single pushed route with inline parameter ───────────────────

    [Fact]
    public void Parse_ProductWithSku_ExtractsParam()
    {
        var steps = CreateService().ParseRoute("//main/products/product/seed-tomato");
        Assert.Single(steps);
        Assert.Contains("sku=seed-tomato", steps[0].route);
        Assert.Contains("//main/products/product", steps[0].route);
    }

    [Fact]
    public void Parse_OrderWithId_ExtractsParam()
    {
        var steps = CreateService().ParseRoute("//main/orders/order/ORD-00001");
        Assert.Single(steps);
        Assert.Contains("orderId=ORD-00001", steps[0].route);
        Assert.Contains("//main/orders/order", steps[0].route);
    }

    [Fact]
    public void Parse_ProductWithUrlEncodedSku_DecodesValue()
    {
        var steps = CreateService().ParseRoute("//main/products/product/seed%20tomato");
        Assert.Single(steps);
        Assert.Contains("sku=seed%20tomato", steps[0].route);
    }

    // ── Pushed route without parameter value ────────────────────────

    [Fact]
    public void Parse_ProductWithoutValue_NoBareParam()
    {
        var steps = CreateService().ParseRoute("//main/products/product");
        Assert.Single(steps);
        Assert.DoesNotContain("?", steps[0].route);
    }

    [Fact]
    public void Parse_CartNoParams_SingleStep()
    {
        var steps = CreateService().ParseRoute("//main/products/cart");
        Assert.Single(steps);
    }

    // ── Nested pushed routes (product → review) ─────────────────────

    [Fact]
    public void Parse_ProductThenReview_TwoSteps()
    {
        var steps = CreateService().ParseRoute("//main/products/product/seed-tomato/review");
        Assert.Equal(2, steps.Count);
        Assert.Contains("//main/products/product", steps[0].route);
        Assert.Contains("sku=seed-tomato", steps[0].route);
        Assert.StartsWith("review", steps[1].route);
        Assert.Contains("sku=seed-tomato", steps[1].route);
    }

    [Fact]
    public void Parse_ProductThenReviewBothHaveSku_ReviewAlsoGetsParam()
    {
        var steps = CreateService().ParseRoute("//main/products/product/seed-basil/review");
        Assert.Equal(2, steps.Count);
        Assert.Contains("sku=seed-basil", steps[0].route);
        Assert.Contains("sku=seed-basil", steps[1].route);
    }

    // ── Relative routes ─────────────────────────────────────────────

    [Fact]
    public void Parse_RelativeBack_PassesThrough()
    {
        var steps = CreateService().ParseRoute("..");
        Assert.Single(steps);
        Assert.Equal("..", steps[0].route);
    }

    [Fact]
    public void Parse_RelativeSingleSegment_PassesThrough()
    {
        var steps = CreateService().ParseRoute("cart");
        Assert.Single(steps);
        Assert.Equal("cart", steps[0].route);
    }

    [Fact]
    public void Parse_RelativeProductWithValue_ExtractsParam()
    {
        var steps = CreateService().ParseRoute("product/seed-tomato");
        Assert.Single(steps);
        Assert.Contains("sku=seed-tomato", steps[0].route);
    }

    [Fact]
    public void Parse_RelativeReviewOnly_PassesThrough()
    {
        var steps = CreateService().ParseRoute("review");
        Assert.Single(steps);
        Assert.Equal("review", steps[0].route);
    }

    // ── Routes with explicit query strings (pass through) ───────────

    [Fact]
    public void Parse_ExplicitQueryString_PreservesIt()
    {
        var steps = CreateService().ParseRoute("//main/products/product?sku=seed-tomato");
        Assert.Single(steps);
        Assert.Equal("//main/products/product?sku=seed-tomato", steps[0].route);
    }

    [Fact]
    public void Parse_ExplicitQueryStringWithExtra_PreservesAll()
    {
        var steps = CreateService().ParseRoute("//main/products/product?sku=seed-tomato&highlight=true");
        Assert.Single(steps);
        Assert.Contains("sku=seed-tomato", steps[0].route);
        Assert.Contains("highlight=true", steps[0].route);
    }

    // ── Edge cases ──────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyString_SingleEmptyStep()
    {
        var steps = CreateService().ParseRoute("");
        Assert.Single(steps);
        Assert.Equal("", steps[0].route);
    }

    [Fact]
    public void Parse_JustSlashes_PassesThrough()
    {
        var steps = CreateService().ParseRoute("//main");
        Assert.Single(steps);
        Assert.Equal("//main", steps[0].route);
    }

    // ── Output is always a valid URI (no double ?) ──────────────────

    [Fact]
    public void Parse_AllOutputSteps_HaveAtMostOneQuestionMark()
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
            var steps = svc.ParseRoute(uri);
            foreach (var step in steps)
            {
                var qCount = step.route.Count(c => c == '?');
                Assert.True(qCount <= 1,
                    $"Route step '{step.route}' (from '{uri}') has {qCount} '?' characters");
            }
        }
    }

    // ── Back-navigation stack correctness ───────────────────────────

    [Fact]
    public void Parse_ProductReview_Step1IsAbsoluteToProduct()
    {
        var steps = CreateService().ParseRoute("//main/products/product/seed-tomato/review");
        Assert.StartsWith("//main/products/product", steps[0].route);
    }

    [Fact]
    public void Parse_ProductReview_Step2IsRelativeReview()
    {
        var steps = CreateService().ParseRoute("//main/products/product/seed-tomato/review");
        Assert.Equal(2, steps.Count);
        Assert.DoesNotContain("//", steps[1].route);
        Assert.StartsWith("review", steps[1].route);
    }

    [Fact]
    public void Parse_ProductReview_BackStackIsThreeLevels()
    {
        var steps = CreateService().ParseRoute("//main/products/product/seed-tomato/review");
        Assert.True(steps[0].route.StartsWith("//"), "Step 1 should be absolute");
        Assert.False(steps[1].route.StartsWith("//"), "Step 2 should be relative");
    }

    [Fact]
    public void Parse_OrderDetail_BackToOrders()
    {
        var steps = CreateService().ParseRoute("//main/orders/order/ORD-00001");
        Assert.Single(steps);
        Assert.StartsWith("//main/orders/order", steps[0].route);
    }

    [Fact]
    public void Parse_ProductOnly_BackToProducts()
    {
        var steps = CreateService().ParseRoute("//main/products/product/seed-tomato");
        Assert.Single(steps);
        Assert.StartsWith("//main/products/product", steps[0].route);
    }

    [Fact]
    public void Parse_BackRoute_IsPassedThrough()
    {
        var steps = CreateService().ParseRoute("..");
        Assert.Single(steps);
        Assert.Equal("..", steps[0].route);
    }

    [Fact]
    public void Parse_MultipleBackRoutes_PassedThrough()
    {
        var steps = CreateService().ParseRoute("../..");
        Assert.Single(steps);
        Assert.Equal("../..", steps[0].route);
    }

    // ── Param propagation to child steps ────────────────────────────

    [Fact]
    public void Parse_ProductReview_BothStepsHaveSku()
    {
        var steps = CreateService().ParseRoute("//main/products/product/seed-tomato/review");
        Assert.Equal(2, steps.Count);
        Assert.Contains("sku=seed-tomato", steps[0].route);
        Assert.Contains("sku=seed-tomato", steps[1].route);
    }

    [Fact]
    public void Parse_ProductReview_BothStepsHaveSameSkuValue()
    {
        var steps = CreateService().ParseRoute("//main/products/product/seed-tomato/review");
        var step0Sku = ExtractQueryParam(steps[0].route, "sku");
        var step1Sku = ExtractQueryParam(steps[1].route, "sku");
        Assert.Equal(step0Sku, step1Sku);
    }

    private static string? ExtractQueryParam(string route, string key)
    {
        var qIdx = route.IndexOf('?');
        if (qIdx < 0) return null;
        var query = route[(qIdx + 1)..];
        foreach (var part in query.Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq] == key)
                return Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        return null;
    }
}
