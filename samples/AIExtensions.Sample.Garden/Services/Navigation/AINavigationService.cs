using System.ComponentModel;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.Navigation;

namespace AIExtensions.Sample.Garden.Services;

/// <summary>
/// Thin wrapper that adds <c>[ExportAIFunction]</c> to the library's
/// <see cref="ShellNavigationService"/>. The library itself has no
/// dependency on AI.Attributes — this wrapper bridges the two.
/// </summary>
public sealed class AINavigationService
{
    private readonly ShellNavigationService _inner;

    public AINavigationService(ShellNavigationService inner) => _inner = inner;

    [ExportAIFunction("get_routes")]
    [Description("Lists all available navigation routes in the app with their full paths and query parameters. Use this to discover where you can navigate and what parameters each page accepts.")]
    public IReadOnlyList<RouteInfo> GetRoutes() => _inner.GetRoutes();

    [ExportAIFunction("get_current_route")]
    [Description("Returns the current Shell navigation location as a URI string.")]
    public string GetCurrentRoute() => _inner.GetCurrentRoute();

    [ExportAIFunction("navigate")]
    [Description("Navigate to a page using a clean URI. Put parameter values directly in the path after the route segment that accepts them. Examples: '//main/products/product/seed-tomato' (product detail), '//main/products/product/seed-tomato/review' (review for that product), '//main/orders/order/ORD-00001' (order detail). Special: '..' (back), '//main/chat' (home), 'cart' (modal).")]
    public Task<string> NavigateAsync(
        [Description("Clean URI with parameter values inline in the path. Examples: '//main/products/product/seed-tomato', '//main/products/product/seed-basil/review', '//main/orders/order/ORD-00001'.")]
        string route) => _inner.NavigateAsync(route);
}
